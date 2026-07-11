using System.ComponentModel.DataAnnotations;

namespace Service;

public sealed class EmbeddingOptions
{
    public bool Enabled { get; init; } = true;
    [Required] public string ModelPath { get; init; } = "Data/model/model.onnx";
    [Range(1, 50)] public int BatchSize { get; init; } = 10;
    [Range(5, 3600)] public int PollSeconds { get; init; } = 120;
    [Range(1, 10)] public int MaxAttempts { get; init; } = 3;
}
