namespace GallerySiteIntegrationTests;

internal sealed class InteractionReadBenchmarkAttribute : FactAttribute
{
    public InteractionReadBenchmarkAttribute()
    {
        if (Environment.GetEnvironmentVariable("GALLERY_INTERACTION_READ_BENCHMARK") != "1")
            Skip = "Set GALLERY_INTERACTION_READ_BENCHMARK=1 and run this test alone for synthetic before/after measurements.";
    }
}
