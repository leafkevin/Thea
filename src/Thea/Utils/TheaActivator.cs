using System;
using System.Reflection;

namespace Thea;

public static class TheaActivator
{
    public static T CreateInstance<T>(IServiceProvider provider, params object[] parameters)
    {
        return (T)CreateInstance(provider, typeof(T), parameters);
    }
    public static object CreateInstance(IServiceProvider provider, Type instanceType, params object[] parameters)
    {
        int bestLength = -1;

        ConstructorMatcher bestMatcher = default;
        if (!instanceType.GetTypeInfo().IsAbstract)
        {
            var ctors = instanceType.GetTypeInfo().DeclaredConstructors;
            foreach (var ctor in ctors)
            {
                if (!ctor.IsStatic && ctor.IsPublic)
                {
                    var matcher = new ConstructorMatcher(ctor);
                    var length = matcher.Match(parameters);

                    if (bestLength < length)
                    {
                        bestLength = length;
                        bestMatcher = matcher;
                    }
                }
            }
        }
        if (bestLength == -1)
        {
            var message = $"A suitable constructor for type '{instanceType}' could not be located. Ensure the type is concrete and services are registered for all parameters of a public constructor.";
            throw new InvalidOperationException(message);
        }
        return bestMatcher.CreateInstance(provider);
    }
    private struct ConstructorMatcher
    {
        private static readonly Type _nullable = typeof(Nullable<>);
        private readonly ConstructorInfo _constructor;
        private readonly ParameterInfo[] _parameters;
        private readonly object[] _parameterValues;

        public ConstructorMatcher(ConstructorInfo constructor)
        {
            _constructor = constructor;
            _parameters = _constructor.GetParameters();
            _parameterValues = new object[_parameters.Length];
        }

        public int Match(object[] givenParameters)
        {
            var applyIndexStart = 0;
            var applyExactLength = 0;
            for (var givenIndex = 0; givenIndex != givenParameters.Length; givenIndex++)
            {
                var givenType = givenParameters[givenIndex]?.GetType().GetTypeInfo();
                var givenMatched = false;

                for (var applyIndex = applyIndexStart; givenMatched == false && applyIndex != _parameters.Length; ++applyIndex)
                {
                    if (_parameterValues[applyIndex] == null &&
                        _parameters[applyIndex].ParameterType.GetTypeInfo().IsAssignableFrom(givenType))
                    {
                        givenMatched = true;
                        _parameterValues[applyIndex] = givenParameters[givenIndex];
                        if (applyIndexStart == applyIndex)
                        {
                            applyIndexStart++;
                            if (applyIndex == givenIndex)
                            {
                                applyExactLength = applyIndex;
                            }
                        }
                    }
                }
                if (givenMatched == false)
                {
                    return -1;
                }
            }
            return applyExactLength;
        }
        public object CreateInstance(IServiceProvider provider)
        {
            for (var index = 0; index != _parameters.Length; index++)
            {
                if (_parameterValues[index] == null)
                {
                    var value = provider.GetService(_parameters[index].ParameterType);
                    if (value == null)
                    {
                        if (!TryGetDefaultValue(_parameters[index], out var defaultValue))
                        {
                            throw new InvalidOperationException($"Unable to resolve service for type '{_parameters[index].ParameterType}' while attempting to activate '{_constructor.DeclaringType}'.");
                        }
                        else
                        {
                            _parameterValues[index] = defaultValue;
                        }
                    }
                    else
                    {
                        _parameterValues[index] = value;
                    }
                }
            }
            return _constructor.Invoke(BindingFlags.DoNotWrapExceptions, binder: null, parameters: _parameterValues, culture: null);
        }
        private static bool TryGetDefaultValue(ParameterInfo parameter, out object defaultValue)
        {
            bool hasDefaultValue;
            var tryToGetDefaultValue = true;
            defaultValue = null;

            try
            {
                hasDefaultValue = parameter.HasDefaultValue;
            }
            catch (FormatException) when (parameter.ParameterType == typeof(DateTime))
            {
                // Workaround for https://github.com/dotnet/corefx/issues/12338
                // If HasDefaultValue throws FormatException for DateTime
                // we expect it to have default value
                hasDefaultValue = true;
                tryToGetDefaultValue = false;
            }

            if (hasDefaultValue)
            {
                if (tryToGetDefaultValue)
                {
                    defaultValue = parameter.DefaultValue;
                }

                // Workaround for https://github.com/dotnet/corefx/issues/11797
                if (defaultValue == null && parameter.ParameterType.IsValueType)
                {
                    defaultValue = Activator.CreateInstance(parameter.ParameterType);
                }

                // Handle nullable enums
                if (defaultValue != null &&
                    parameter.ParameterType.IsGenericType &&
                    parameter.ParameterType.GetGenericTypeDefinition() == _nullable
                    )
                {
                    var underlyingType = Nullable.GetUnderlyingType(parameter.ParameterType);
                    if (underlyingType != null && underlyingType.IsEnum)
                    {
                        defaultValue = Enum.ToObject(underlyingType, defaultValue);
                    }
                }
            }
            return hasDefaultValue;
        }
    }
}
