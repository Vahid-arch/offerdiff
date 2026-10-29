namespace OfferDiff.Core;

public record FeedItem(int Row, string Id, string Title, string Link, Price Price, Price? SalePrice, string Availability);

public record FeedError(int Row, string Message);

public record FeedReadResult(List<FeedItem> Items, List<FeedError> Errors);
