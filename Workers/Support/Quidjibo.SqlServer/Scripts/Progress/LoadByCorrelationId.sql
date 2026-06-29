SELECT [Id], [WorkId], [CorrelationId], [Name], [Queue], [Note], [Value], [RecordedOn]
FROM [Quidjibo].[Progress]  WITH (READPAST)
WHERE [CorrelationId] = @CorrelationId