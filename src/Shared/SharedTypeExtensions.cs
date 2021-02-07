// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using JB = JetBrains.Annotations;

#nullable enable

// ReSharper disable once CheckNamespace
namespace System
{
    [DebuggerStepThrough]
    internal static class SharedTypeExtensions
    {
        private static readonly Dictionary<Type, string> _builtInTypeNames = new()
        {
            { typeof(bool), "bool" },
            { typeof(byte), "byte" },
            { typeof(char), "char" },
            { typeof(decimal), "decimal" },
            { typeof(double), "double" },
            { typeof(float), "float" },
            { typeof(int), "int" },
            { typeof(long), "long" },
            { typeof(object), "object" },
            { typeof(sbyte), "sbyte" },
            { typeof(short), "short" },
            { typeof(string), "string" },
            { typeof(uint), "uint" },
            { typeof(ulong), "ulong" },
            { typeof(ushort), "ushort" },
            { typeof(void), "void" }
        };

        public static Type UnwrapNullableType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => Nullable.GetUnderlyingType(type) ?? type;

        public static bool IsNullableValueType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);

        public static bool IsNullableType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => !type.IsValueType || type.IsNullableValueType();

        public static bool IsValidEntityType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => type.IsClass;

        public static bool IsPropertyBagType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            if (type.IsGenericTypeDefinition)
            {
                return false;
            }

            var types = GetGenericTypeImplementations(type, typeof(IDictionary<,>));
            return types.Any(
                t => t.GetGenericArguments()[0] == typeof(string)
                    && t.GetGenericArguments()[1] == typeof(object));
        }

        public static Type MakeNullable(this Type type, bool nullable = true)
            => type.IsNullableType() == nullable
                ? type
                : nullable
                    ? typeof(Nullable<>).MakeGenericType(type)
                    : type.UnwrapNullableType();

        public static bool IsNumeric([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            type = type.UnwrapNullableType();

            return type.IsInteger()
                || type == typeof(decimal)
                || type == typeof(float)
                || type == typeof(double);
        }

        public static bool IsInteger([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            type = type.UnwrapNullableType();

            return type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(byte)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort)
                || type == typeof(sbyte)
                || type == typeof(char);
        }

        public static bool IsSignedInteger([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(sbyte);

        public static bool IsAnonymousType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => type.Name.StartsWith("<>", StringComparison.Ordinal)
                && type.GetCustomAttributes(typeof(CompilerGeneratedAttribute), inherit: false).Length > 0
                && type.Name.Contains("AnonymousType");

        public static bool IsTupleType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            if (type == typeof(Tuple))
            {
                return true;
            }

            if (type.IsGenericType)
            {
                var genericDefinition = type.GetGenericTypeDefinition();
                if (genericDefinition == typeof(Tuple<>)
                    || genericDefinition == typeof(Tuple<,>)
                    || genericDefinition == typeof(Tuple<,,>)
                    || genericDefinition == typeof(Tuple<,,,>)
                    || genericDefinition == typeof(Tuple<,,,,>)
                    || genericDefinition == typeof(Tuple<,,,,,>)
                    || genericDefinition == typeof(Tuple<,,,,,,>)
                    || genericDefinition == typeof(Tuple<,,,,,,,>)
                    || genericDefinition == typeof(Tuple<,,,,,,,>))
                {
                    return true;
                }
            }

            return false;
        }

        public static PropertyInfo? GetAnyProperty(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
            this Type type,
            string name)
        {
            var props = type.GetRuntimeProperties().Where(p => p.Name == name).ToList();
            if (props.Count > 1)
            {
                throw new AmbiguousMatchException();
            }

            return props.SingleOrDefault();
        }

        public static MethodInfo GetRequiredMethod(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] this Type type,
            string name,
            params Type[] parameters)
        {
            var method = type.GetTypeInfo().GetMethod(name, parameters);

            if (method == null
                && parameters.Length == 0)
            {
                method = type.GetMethod(name);
            }

            if (method == null)
            {
                throw new InvalidOperationException();
            }

            return method;
        }

        public static PropertyInfo GetRequiredProperty(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] this Type type,
            string name)
        {
            var property = type.GetTypeInfo().GetProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException();
            }

            return property;
        }

        public static FieldInfo GetRequiredDeclaredField(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields)]
            this Type type,
            string name)
        {
            var field = type.GetTypeInfo().GetDeclaredField(name);
            if (field == null)
            {
                throw new InvalidOperationException();
            }

            return field;
        }

        public static MethodInfo GetRequiredDeclaredMethod(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
            this Type type,
            string name)
        {
            var method = type.GetTypeInfo().GetDeclaredMethod(name);
            if (method == null)
            {
                throw new InvalidOperationException();
            }

            return method;
        }

        public static PropertyInfo GetRequiredDeclaredProperty(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
            this Type type,
            string name)
        {
            var property = type.GetTypeInfo().GetDeclaredProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException();
            }

            return property;
        }

        public static MethodInfo GetRequiredRuntimeMethod(
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] this Type type,
                string name,
                params Type[] parameters)
        {
            var method = type.GetTypeInfo().GetRuntimeMethod(name, parameters);
            if (method == null)
            {
                throw new InvalidOperationException();
            }

            return method;
        }

        public static PropertyInfo GetRequiredRuntimeProperty(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] this Type type,
            string name)
        {
            var property = type.GetTypeInfo().GetRuntimeProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException();
            }

            return property;
        }

        public static bool IsInstantiable([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => !type.IsAbstract
                && !type.IsInterface
                && (!type.IsGenericType || !type.IsGenericTypeDefinition);

        public static Type UnwrapEnumType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            var isNullable = type.IsNullableType();
            var underlyingNonNullableType = isNullable ? type.UnwrapNullableType() : type;
            if (!underlyingNonNullableType.IsEnum)
            {
                return type;
            }

            var underlyingEnumType = Enum.GetUnderlyingType(underlyingNonNullableType);
            return isNullable ? MakeNullable(underlyingEnumType) : underlyingEnumType;
        }

        public static Type GetSequenceType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            var sequenceType = TryGetSequenceType(type);
            if (sequenceType == null)
            {
                // TODO: Add exception message
                throw new ArgumentException();
            }

            return sequenceType;
        }

#nullable enable

        public static Type? TryGetSequenceType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => type.TryGetElementType(typeof(IEnumerable<>))
                ?? type.TryGetElementType(typeof(IAsyncEnumerable<>));

        public static Type? TryGetElementType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type,
            Type interfaceOrBaseType)
        {
            if (type.IsGenericTypeDefinition)
            {
                return null;
            }

            var types = GetGenericTypeImplementations(type, interfaceOrBaseType);

            Type? singleImplementation = null;
            foreach (var implementation in types)
            {
                if (singleImplementation == null)
                {
                    singleImplementation = implementation;
                }
                else
                {
                    singleImplementation = null;
                    break;
                }
            }

            return singleImplementation?.GenericTypeArguments.FirstOrDefault();
        }

#nullable disable

        public static bool IsCompatibleWith(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type propertyType,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type fieldType)
        {
            if (propertyType.IsAssignableFrom(fieldType)
                || fieldType.IsAssignableFrom(propertyType))
            {
                return true;
            }

            var propertyElementType = propertyType.TryGetSequenceType();
            var fieldElementType = fieldType.TryGetSequenceType();

            return propertyElementType != null
                && fieldElementType != null
                && IsCompatibleWith(propertyElementType, fieldElementType);
        }

        public static IEnumerable<Type> GetGenericTypeImplementations(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type interfaceOrBaseType)
        {
            var typeInfo = type.GetTypeInfo();
            if (!typeInfo.IsGenericTypeDefinition)
            {
                var baseTypes = interfaceOrBaseType.GetTypeInfo().IsInterface
                    ? typeInfo.ImplementedInterfaces
                    : type.GetBaseTypes();
                foreach (var baseType in baseTypes)
                {
                    if (baseType.IsGenericType
                        && baseType.GetGenericTypeDefinition() == interfaceOrBaseType)
                    {
                        yield return baseType;
                    }
                }

                if (type.IsGenericType
                    && type.GetGenericTypeDefinition() == interfaceOrBaseType)
                {
                    yield return type;
                }
            }
        }

        public static IEnumerable<Type> GetBaseTypes([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            type = type.BaseType;

            while (type != null)
            {
                yield return type;

                type = type.BaseType;
            }
        }

        public static IEnumerable<Type> GetTypesInHierarchy(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            while (type != null)
            {
                yield return type;

                type = type.BaseType;
            }
        }

        public static ConstructorInfo GetDeclaredConstructor(
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            this Type type,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type[] types)
        {
            types ??= Array.Empty<Type>();

            return type.GetTypeInfo().DeclaredConstructors
                .SingleOrDefault(
                    c => !c.IsStatic
                        && c.GetParameters().Select(p => p.ParameterType).SequenceEqual(types));
        }

        public static IEnumerable<PropertyInfo> GetPropertiesInHierarchy(
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.NonPublicProperties)]
            this Type type,
            string name)
        {
            do
            {
                var typeInfo = type.GetTypeInfo();
                foreach (var propertyInfo in typeInfo.DeclaredProperties)
                {
                    if (propertyInfo.Name.Equals(name, StringComparison.Ordinal)
                        && !(propertyInfo.GetMethod ?? propertyInfo.SetMethod).IsStatic)
                    {
                        yield return propertyInfo;
                    }
                }

                type = typeInfo.BaseType;
            }
            while (type != null);
        }

        // Looking up the members through the whole hierarchy allows to find inherited private members.
        [RequiresUnreferencedCodeAttribute("TODO-TRIMMING")]
        public static IEnumerable<MemberInfo> GetMembersInHierarchy(
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicProperties
                | DynamicallyAccessedMemberTypes.NonPublicProperties
                | DynamicallyAccessedMemberTypes.PublicFields
                | DynamicallyAccessedMemberTypes.NonPublicFields)]
            this Type type)
        {
            do
            {
                // Do the whole hierarchy for properties first since looking for fields is slower.
                foreach (var propertyInfo in type.GetRuntimeProperties().Where(pi => !(pi.GetMethod ?? pi.SetMethod).IsStatic))
                {
                    yield return propertyInfo;
                }

                foreach (var fieldInfo in type.GetRuntimeFields().Where(f => !f.IsStatic))
                {
                    yield return fieldInfo;
                }

                type = type.BaseType;
            }
            while (type != null);
        }

        [RequiresUnreferencedCodeAttribute("TODO-TRIMMING")]
        public static IEnumerable<MemberInfo> GetMembersInHierarchy(
            [DynamicallyAccessedMembers(
                DynamicallyAccessedMemberTypes.PublicProperties
                | DynamicallyAccessedMemberTypes.NonPublicProperties
                | DynamicallyAccessedMemberTypes.PublicFields
                | DynamicallyAccessedMemberTypes.NonPublicFields)]
            this Type type,
            string name)
            => type.GetMembersInHierarchy().Where(m => m.Name == name);

        private static readonly Dictionary<Type, object> _commonTypeDictionary = new()
        {
#pragma warning disable IDE0034 // Simplify 'default' expression - default causes default(object)
            { typeof(int), default(int) },
            { typeof(Guid), default(Guid) },
            { typeof(DateTime), default(DateTime) },
            { typeof(DateTimeOffset), default(DateTimeOffset) },
            { typeof(long), default(long) },
            { typeof(bool), default(bool) },
            { typeof(double), default(double) },
            { typeof(short), default(short) },
            { typeof(float), default(float) },
            { typeof(byte), default(byte) },
            { typeof(char), default(char) },
            { typeof(uint), default(uint) },
            { typeof(ushort), default(ushort) },
            { typeof(ulong), default(ulong) },
            { typeof(sbyte), default(sbyte) }
#pragma warning restore IDE0034 // Simplify 'default' expression
        };

        public static object GetDefaultValue(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] this Type type)
        {
            if (!type.IsValueType)
            {
                return null;
            }

            // A bit of perf code to avoid calling Activator.CreateInstance for common types and
            // to avoid boxing on every call. This is about 50% faster than just calling CreateInstance
            // for all value types.
            return _commonTypeDictionary.TryGetValue(type, out var value)
                ? value
                : Activator.CreateInstance(type);
        }

        [RequiresUnreferencedCodeAttribute("TODO-TRIMMING")]
        public static IEnumerable<TypeInfo> GetConstructibleTypes(this Assembly assembly)
            => assembly.GetLoadableDefinedTypes().Where(
                t => !t.IsAbstract
                    && !t.IsGenericTypeDefinition);

        [RequiresUnreferencedCodeAttribute("TODO-TRIMMING")]
        public static IEnumerable<TypeInfo> GetLoadableDefinedTypes(this Assembly assembly)
        {
            try
            {
                return assembly.DefinedTypes;
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null).Select(IntrospectionExtensions.GetTypeInfo);
            }
        }

        public static bool IsQueryableType(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            if (type.IsGenericType
                && type.GetGenericTypeDefinition() == typeof(IQueryable<>))
            {
                return true;
            }

            return type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryable<>));
        }

        /// <summary>
        ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
        ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
        ///     any release. You should only use it directly in your code with extreme caution and knowing that
        ///     doing so can result in application failures when updating to a new Entity Framework Core release.
        /// </summary>
        public static string DisplayName(
            [JB.NotNull, DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type,
            bool fullName = true)
        {
            var stringBuilder = new StringBuilder();
            ProcessType(stringBuilder, type, fullName);
            return stringBuilder.ToString();
        }

        private static void ProcessType(
            StringBuilder builder,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type type,
            bool fullName)
        {
            if (type.IsGenericType)
            {
                var genericArguments = type.GetGenericArguments();
                ProcessGenericType(builder, type, genericArguments, genericArguments.Length, fullName);
            }
            else if (type.IsArray)
            {
                ProcessArrayType(builder, type, fullName);
            }
            else if (_builtInTypeNames.TryGetValue(type, out var builtInName))
            {
                builder.Append(builtInName);
            }
            else if (!type.IsGenericParameter)
            {
                builder.Append(fullName ? type.FullName : type.Name);
            }

            static void ProcessArrayType(
                StringBuilder builder,
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type type,
                bool fullName)
            {
                var innerType = type;
                while (innerType.IsArray)
                {
                    innerType = innerType.GetElementType();
                }

                ProcessType(builder, innerType, fullName);

                while (type.IsArray)
                {
                    builder.Append('[');
                    builder.Append(',', type.GetArrayRank() - 1);
                    builder.Append(']');
                    type = type.GetElementType();
                }
            }

            static void ProcessGenericType(
                StringBuilder builder,
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type type,
                [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] Type[] genericArguments,
                int length,
                bool fullName)
            {
                var offset = type.IsNested ? type.DeclaringType.GetGenericArguments().Length : 0;

                if (fullName)
                {
                    if (type.IsNested)
                    {
                        ProcessGenericType(builder, type.DeclaringType, genericArguments, offset, fullName);
                        builder.Append('+');
                    }
                    else
                    {
                        builder.Append(type.Namespace);
                        builder.Append('.');
                    }
                }

                var genericPartIndex = type.Name.IndexOf('`');
                if (genericPartIndex <= 0)
                {
                    builder.Append(type.Name);
                    return;
                }

                builder.Append(type.Name, 0, genericPartIndex);
                builder.Append('<');

                for (var i = offset; i < length; i++)
                {
                    ProcessType(builder, genericArguments[i], fullName);
                    if (i + 1 == length)
                    {
                        continue;
                    }

                    builder.Append(',');
                    if (!genericArguments[i + 1].IsGenericParameter)
                    {
                        builder.Append(' ');
                    }
                }

                builder.Append('>');
            }
        }

        public static IEnumerable<string> GetNamespaces(
            [JB.NotNull,DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
        {
            if (_builtInTypeNames.ContainsKey(type))
            {
                yield break;
            }

            yield return type.Namespace;

            if (type.IsGenericType)
            {
                foreach (var typeArgument in type.GenericTypeArguments)
                {
                    foreach (var ns in typeArgument.GetNamespaces())
                    {
                        yield return ns;
                    }
                }
            }
        }

        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2085:UnrecognizedReflectionPattern",
            Justification = "TODO-TRIMMING, probably correct to suppress but not sure")]
        public static ConstantExpression GetDefaultValueConstant(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.None)] this Type type)
            => (ConstantExpression)_generateDefaultValueConstantMethod
                .MakeGenericMethod(type).Invoke(null, Array.Empty<object>());

        private static readonly MethodInfo _generateDefaultValueConstantMethod =
            typeof(SharedTypeExtensions).GetTypeInfo().GetDeclaredMethod(nameof(GenerateDefaultValueConstant));

        private static ConstantExpression GenerateDefaultValueConstant<TDefault>()
            => Expression.Constant(default(TDefault), typeof(TDefault));
    }
}
