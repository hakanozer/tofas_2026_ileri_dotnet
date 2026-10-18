namespace EvolutionaryLogistics.Services.Ai.Contracts;

/// <summary>Asistana yöneltilen serbest metin sorusu.</summary>
public sealed record AskRequest(string Prompt);

/// <summary>Asistanın ürettiği yanıt. Uydurma yasaktır; bilinmeyen durumda "Bu bilgi elimde yok" döner.</summary>
public sealed record AskResponse(string Answer);

/// <summary>RAG ingest isteği: kaynak adı + ham doküman içeriği.</summary>
public sealed record IngestRequest(string Source, string Content);

/// <summary>Stok seviyesi — typed, SQL'siz tool sözleşmesi.</summary>
public sealed record InventoryLevel(string Sku, int Quantity, string Warehouse);

/// <summary>Stok tükenme tahmini — deterministic demo çıktısı.</summary>
public sealed record StockoutForecast(string Sku, DateOnly EstimatedStockoutDate, double DailyVelocity);

/// <summary>Sipariş durumu — typed tool sözleşmesi.</summary>
public sealed record OrderStatus(string OrderId, string Status, string CustomerId);
