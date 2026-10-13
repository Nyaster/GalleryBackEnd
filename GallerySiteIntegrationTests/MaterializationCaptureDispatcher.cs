using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GallerySiteIntegrationTests;

internal interface IMaterializationObserver
{
    void EntityMaterialized(object entity);
}

// EF keys its internal service provider on singleton interceptors. Keep this instance stable,
// and route events to collectors by context without keeping disposed contexts alive.
internal sealed class MaterializationCaptureDispatcher : IMaterializationInterceptor
{
    internal static readonly MaterializationCaptureDispatcher Instance = new();
    private readonly ConditionalWeakTable<DbContext, IMaterializationObserver[]> _observers = new();

    private MaterializationCaptureDispatcher() { }

    internal void Register(DbContext context, IMaterializationObserver[] observers)
        => _observers.Add(context, observers);

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (_observers.TryGetValue(materializationData.Context, out var observers))
            foreach (var observer in observers)
                observer.EntityMaterialized(entity);
        return entity;
    }
}
