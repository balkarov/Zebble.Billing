﻿namespace Zebble.Billing
{
    using System.Threading.Tasks;

    public interface IProductRepository
    {
        Task<Product[]> GetProducts();
        Task<Product> GetById(string productId);
    }

    public interface ITransactionRepository
    {
        Task<Transaction> Save(Transaction transaction);
    }
}
