﻿namespace Zebble.Billing
{
    using System;
    using System.Threading.Tasks;
    using Android.Content;
    using Huawei.Hmf.Extensions;
    using Huawei.Hms.Iap;
    using Huawei.Hms.Iap.Entity;
    using Olive;

    class PurchaseSubscriptionCommand : StoreCommandBase<(PurchaseResult, string)>
    {
        const int ResultCode = 6666;

        static TaskCompletionSource<(PurchaseResult, string)> Source;

        readonly Product Product;

        public PurchaseSubscriptionCommand(Product product) => Product = product;

        protected override async Task<(PurchaseResult, string)> DoExecute(IBillingUser user)
        {
            var context = BillingContext.Current;

            try
            {
                await context.Refresh(user);

                if (context.IsSubscribed && (await context.CurrentProduct)?.Id == Product.Id)
                    return (PurchaseResult.AlreadySubscribed, null);

                var request = new PurchaseIntentReq
                {
                    PriceType = Product.GetPriceType(),
                    ProductId = Product.Id,
                };

                var purchase = await Billing.CreatePurchaseIntent(request).AsAsync<PurchaseIntentResult>();
                var status = purchase.Status;

                if (!status.IsSuccess)
                    return (PurchaseResult.NotCompleted, null);

                if (status.HasResolution == false)
                    return (PurchaseResult.NotCompleted, null);

                Source?.TrySetCanceled();
                Source = new();
                status.StartResolutionForResult(UIRuntime.CurrentActivity, ResultCode);

                return await Source.Task;
            }
            catch (Exception ex)
            {
                Log.For(this).Error(ex);
                return (PurchaseResult.Unknown, null);
            }
        }

        public static async Task HandlePurchaseResult(
            int requestCode,
            Intent data,
            IBillingUser user)
        {
            if (requestCode != ResultCode) return;

            if (data is null) return;

            var context = BillingContext.Current;

            try
            {
                var billing = Iap.GetIapClient(UIRuntime.CurrentActivity);

                var purchaseResult = billing.ParsePurchaseResultInfoFromIntent(data);

                if (purchaseResult.ReturnCode == OrderStatusCode.OrderStateFailed)
                {
                    Source.SetResult((PurchaseResult.NotCompleted, null));
                    return;
                }

                if (purchaseResult.ReturnCode == OrderStatusCode.OrderStateCancel)
                {
                    Source.SetResult((PurchaseResult.UserCancelled, null));
                    return;
                }

                if (purchaseResult.ReturnCode == OrderStatusCode.OrderProductOwned)
                {
                    await context.RestoreSubscription(user, userRequest: false);

                    if (context.IsSubscribed)
                    {
                        Source.SetResult((PurchaseResult.Succeeded, null));
                        return;
                    }

                    Source.SetResult((PurchaseResult.AlreadySubscribed, null));
                    return;
                }
                else if (purchaseResult.ReturnCode != OrderStatusCode.OrderStateSuccess)
                {
                    Source.SetResult((PurchaseResult.Unknown, null));
                    return;
                }

                var purchase = new InAppPurchaseData(purchaseResult.InAppPurchaseData);
                var (result, originUserId) = await context.ProcessPurchase(user, purchase);

                if (result != PurchaseResult.Succeeded)
                {
                    Source.SetResult((result, null));
                    return;
                }

                await context.Refresh(user);

                if (context.IsSubscribed)
                {
                    Source.SetResult((PurchaseResult.Succeeded, originUserId));
                    return;
                }

                Source.SetResult((PurchaseResult.WillBeActivated, originUserId));
            }
            catch (Exception ex)
            {
                Log.For<PurchaseSubscriptionCommand>().Error(ex);
                Source.SetResult((PurchaseResult.NotCompleted, null));
                return;
            }
        }
    }
}