using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Pgvector;
using Service.Contracts;
using SkiaSharp;

namespace Service;

public sealed class OnnxImageEmbeddingGenerator : IImageEmbeddingGenerator, IDisposable
{
    private readonly InferenceSession _session;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public OnnxImageEmbeddingGenerator(IOptions<EmbeddingOptions> options)
    {
        var modelPath = Path.GetFullPath(options.Value.ModelPath);
        if (!File.Exists(modelPath))
            throw new InvalidOperationException($"Embedding model was not found at '{modelPath}'.");
        _session = new InferenceSession(modelPath, new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        });
    }

    public async Task<Vector> GenerateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var source = SKBitmap.Decode(imageStream) ?? throw new InvalidOperationException("Image cannot be decoded for embedding.");
            using var canvasBitmap = new SKBitmap(512, 512, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(canvasBitmap))
            {
                canvas.Clear(SKColors.Black);
                var scale = Math.Min(512f / source.Width, 512f / source.Height);
                var width = source.Width * scale;
                var height = source.Height * scale;
                canvas.DrawBitmap(source, new SKRect((512 - width) / 2, (512 - height) / 2,
                    (512 + width) / 2, (512 + height) / 2),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }

            var tensor = new DenseTensor<float>([1, 3, 512, 512]);
            for (var y = 0; y < 512; y++)
            for (var x = 0; x < 512; x++)
            {
                var pixel = canvasBitmap.GetPixel(x, y);
                tensor[0, 0, y, x] = pixel.Red / 255f;
                tensor[0, 1, y, x] = pixel.Green / 255f;
                tensor[0, 2, y, x] = pixel.Blue / 255f;
            }

            using var input = OrtValue.CreateTensorValueFromMemory(OrtMemoryInfo.DefaultInstance, tensor.Buffer,
                [1, 3, 512, 512]);
            using var results = _session.Run(new RunOptions(), new Dictionary<string, OrtValue>
            {
                ["input"] = input
            }, _session.OutputNames);
            var result = results[0].GetTensorDataAsSpan<float>().ToArray();
            if (result.Length != 1280)
                throw new InvalidOperationException($"Embedding model returned {result.Length} values; 1280 are required.");
            return new Vector(result);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _session.Dispose();
        _gate.Dispose();
    }
}
