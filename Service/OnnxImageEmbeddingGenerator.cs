using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Pgvector;
using Service.Contracts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Service;

public class OnnxImageEmbeddingGenerator : IImageEmbeddingGenerator, IDisposable
{
    private readonly InferenceSession? _session;
    private readonly SemaphoreSlim _semaphoreSlim = new(1, 1);
    private readonly string ModelPath; //todo: Make path from config

    public OnnxImageEmbeddingGenerator()
    {
        ModelPath = $"{Directory.GetCurrentDirectory()}/upload/model/model.onnx";
        try
        {
            var sessionOptions = new SessionOptions();
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            _session = new InferenceSession(ModelPath, sessionOptions);
        }
        catch (Exception e)
        {
            Console.WriteLine();
        }
    }

    public async Task<Vector> GenerateEmbeddingAsync(Stream imageStream, CancellationToken cancellationToken)
    {
        await _semaphoreSlim.WaitAsync(cancellationToken);
        try
        {
            using var image = await Image.LoadAsync<Rgb24>(imageStream, cancellationToken);
            image.Mutate(x =>
                x.Resize(new ResizeOptions()
                {
                    Size = new Size(512, 512),
                    Mode = ResizeMode.Pad
                }));
            var imageTensor = new DenseTensor<float>(new[] { 1, 3, 512, 512 });
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var pixelSpan = accessor.GetRowSpan(y);
                    for (var x = 0; x < accessor.Width; x++)
                    {
                        imageTensor[0, 0, y, x] = pixelSpan[x].R / 255f;
                        imageTensor[0, 1, y, x] = pixelSpan[x].G / 255f;
                        imageTensor[0, 2, y, x] = pixelSpan[x].B / 255f;
                    }
                }
            });
            using var inputOrtValue = OrtValue.CreateTensorValueFromMemory(OrtMemoryInfo.DefaultInstance,
                imageTensor.Buffer,
                [1, 3, 512, 512]);
            var inputs = new Dictionary<string, OrtValue>
            {
                { "input", inputOrtValue }
            };
            using var results = _session.Run(new RunOptions(), inputs, _session.OutputNames);
            var embedding = results[0].GetTensorDataAsSpan<float>().ToArray();
            return new Vector(embedding);
        }
        finally
        {
            _semaphoreSlim.Release();
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _semaphoreSlim?.Dispose();
    }
}