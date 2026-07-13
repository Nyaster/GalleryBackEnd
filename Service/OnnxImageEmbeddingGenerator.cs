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
    private readonly int _inputWidth;
    private readonly int _inputHeight;
    private readonly int _outputDimensions;
    private readonly string _inputName;
    private readonly string _outputName;

    public OnnxImageEmbeddingGenerator(IOptions<EmbeddingOptions> options)
    {
        var settings = options.Value;
        var modelPath = Path.GetFullPath(settings.ModelPath);
        if (!File.Exists(modelPath))
            throw new InvalidOperationException($"Embedding model was not found at '{modelPath}'.");
        _inputWidth = settings.InputWidth;
        _inputHeight = settings.InputHeight;
        _outputDimensions = settings.OutputDimensions;
        _session = new InferenceSession(modelPath, new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        });
        _inputName = _session.InputNames.Single();
        _outputName = _session.OutputNames[0];
    }

    public async Task<Vector> GenerateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var source = SKBitmap.Decode(imageStream) ?? throw new InvalidOperationException("Image cannot be decoded for embedding.");
            using var canvasBitmap = new SKBitmap(_inputWidth, _inputHeight, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (var canvas = new SKCanvas(canvasBitmap))
            {
                canvas.DrawBitmap(source, new SKRect(0, 0, _inputWidth, _inputHeight),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
            }

            var tensor = new DenseTensor<float>([1, 3, _inputHeight, _inputWidth]);
            for (var y = 0; y < _inputHeight; y++)
            for (var x = 0; x < _inputWidth; x++)
            {
                var pixel = canvasBitmap.GetPixel(x, y);
                tensor[0, 0, y, x] = pixel.Red / 127.5f - 1f;
                tensor[0, 1, y, x] = pixel.Green / 127.5f - 1f;
                tensor[0, 2, y, x] = pixel.Blue / 127.5f - 1f;
            }

            using var input = OrtValue.CreateTensorValueFromMemory(OrtMemoryInfo.DefaultInstance, tensor.Buffer,
                [1, 3, _inputHeight, _inputWidth]);
            using var results = _session.Run(new RunOptions(), new Dictionary<string, OrtValue>
            {
                [_inputName] = input
            }, [_outputName]);
            var result = results[0].GetTensorDataAsSpan<float>().ToArray();
            if (result.Length != _outputDimensions)
                throw new InvalidOperationException($"Embedding model returned {result.Length} values; {_outputDimensions} are required.");
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
