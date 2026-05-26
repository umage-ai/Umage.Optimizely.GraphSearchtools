using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace UmageAI.Optimizely.GraphSearchTools.Infrastructure;

/// <summary>
/// MVC's default <see cref="ControllerFeatureProvider"/> only discovers
/// <c>public</c> controllers, which forces every controller in an addon to be
/// part of its public NuGet surface (CS0051 then transitively requires the
/// services and DTOs they reference to be public too). This provider extends
/// discovery to <c>internal</c> controller types declared in the same assembly,
/// so the addon can keep its controllers and their dependencies out of the
/// documented public API.
/// </summary>
internal sealed class InternalControllerFeatureProvider : ControllerFeatureProvider
{
    private static readonly Assembly OwnAssembly = typeof(InternalControllerFeatureProvider).Assembly;

    protected override bool IsController(TypeInfo typeInfo)
    {
        if (base.IsController(typeInfo)) return true;

        // Mirror the default rules but allow internal types in our own assembly.
        if (typeInfo.Assembly != OwnAssembly) return false;
        if (!typeInfo.IsClass) return false;
        if (typeInfo.IsAbstract) return false;
        if (typeInfo.ContainsGenericParameters) return false;
        if (typeInfo.IsDefined(typeof(Microsoft.AspNetCore.Mvc.NonControllerAttribute))) return false;

        var isController =
            typeInfo.Name.EndsWith("Controller", StringComparison.OrdinalIgnoreCase) ||
            typeInfo.IsDefined(typeof(Microsoft.AspNetCore.Mvc.ControllerAttribute));

        return isController;
    }
}
