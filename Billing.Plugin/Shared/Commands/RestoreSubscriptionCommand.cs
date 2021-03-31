﻿namespace Zebble.Billing
{
    using System;
    using System.Threading.Tasks;
    using Plugin.InAppBilling;
    using Olive;

    class RestoreSubscriptionCommand : StoreCommandBase<bool>
    {
        protected override async Task<bool> DoExecute()
        {
            return await Fetch(ItemType.Subscription) | await Fetch(ItemType.InAppPurchase);
        }

        async Task<bool> Fetch(ItemType type)
        {
            try
            {
                var purchases = await Billing.GetPurchasesAsync(type);
                if (purchases.None()) return false;

                foreach (var purchase in purchases.Distinct(x => x.Id))
                {
                    if (purchase.State != PurchaseState.Purchased) continue;
                    if (purchase.IsAcknowledged) continue;

                    await BillingContext.Current.PurchaseAttempt(purchase.ToEventArgs());
                    await Billing.AcknowledgePurchaseAsync(purchase.PurchaseToken);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.For(this).Error(ex);
                return false;
            }
        }
    }
}