using GrabBike.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Repositories;

public interface IQuoteRepository
{
    Task AddAsync(Quote quote, CancellationToken cancellationToken = default);
    Task<Quote?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class QuoteRepository(AppDbContext db) : IQuoteRepository
{
    public async Task AddAsync(Quote quote, CancellationToken cancellationToken = default)
    {
        await db.Quotes.AddAsync(quote, cancellationToken);
    }

    public Task<Quote?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Quotes.FirstOrDefaultAsync(quote => quote.Id == id, cancellationToken);
}
