using System;
using System.Reflection;

namespace Uuvr;

public static class UuvrReflection
{
    // Reflection wraps the real failure in TargetInvocationException (and type initializers in
    // TypeInitializationException), whose own message is the useless "Exception has been thrown
    // by the target of an invocation." Always log the innermost exception instead.
    public static Exception Unwrap(Exception exception)
    {
        while ((exception is TargetInvocationException || exception is TypeInitializationException) &&
               exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    public static string Describe(Exception exception)
    {
        var inner = Unwrap(exception);
        return inner == exception
            ? $"{inner.GetType().Name}: {inner.Message}"
            : $"{exception.GetType().Name} -> {inner.GetType().Name}: {inner.Message}";
    }

    // Under IL2CPP, managed wrappers around game objects carry their *declared* type, not the
    // object's real type: an XRDisplaySubsystemDescriptor pulled out of a List<ISubsystemDescriptor>
    // arrives as an ISubsystemDescriptor wrapper, and GetType() hierarchy checks match nothing.
    // The il2cpp side has to be asked instead, via Il2CppObjectBase.TryCast<T>().
    //
    // Returns the instance as targetType (possibly a new wrapper), or null if it isn't one.
    // On Mono, real runtime types make this a plain IsInstanceOfType check.
    public static object? CastToType(object? instance, Type targetType)
    {
        if (instance == null) return null;
        if (targetType.IsInstanceOfType(instance)) return instance;

        var tryCastMethod = instance.GetType().GetMethod("TryCast", BindingFlags.Public | BindingFlags.Instance);
        if (tryCastMethod == null || !tryCastMethod.IsGenericMethodDefinition) return null;

        try
        {
            return tryCastMethod.MakeGenericMethod(targetType).Invoke(instance, null);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
