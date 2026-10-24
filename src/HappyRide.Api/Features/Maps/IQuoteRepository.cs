namespace HappyRide.Api.Features.Maps;

public interface IQuoteRepository
{
    Task AddAsync(Quote quote, CancellationToken cancellationToken = default);
    Task<Quote?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}
