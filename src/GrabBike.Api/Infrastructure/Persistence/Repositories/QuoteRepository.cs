using GrabBike.Api.Features.Maps;
using Microsoft.EntityFrameworkCore;

namespace GrabBike.Api.Infrastructure.Persistence.Repositories;

public sealed class QuoteRepository(AppDbContext db) : IQuoteRepository
{
    public async Task AddAsync(Quote quote, CancellationToken cancellationToken = default)
    {
        await db.Quotes.AddAsync(quote, cancellationToken);
    }

    public Task<Quote?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Quotes.FirstOrDefaultAsync(quote => quote.Id == id, cancellationToken);
}
