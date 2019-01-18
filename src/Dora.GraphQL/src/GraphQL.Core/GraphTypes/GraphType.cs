﻿using Dora.GraphQL.Resolvers;
using Dora.GraphQL.Schemas;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Dora.GraphQL.GraphTypes
{
    public class GraphType : IEquatable<GraphType>, IGraphType
    {
        private readonly Func<object,object> _valueResolver;
        public Type Type { get; }
        public string Name { get; }
        public bool IsRequired { get; }
        public bool IsEnumerable { get; }
        public IDictionary<string, GraphField> Fields { get; }
        private GraphType(OperationType operationType)
        {
            Type = typeof(void);
            Name = operationType.ToString();
            IsEnumerable = false;
            IsRequired = false;
            Fields = new Dictionary<string, GraphField>();
        }

        internal GraphType(
            IAttributeAccessor attributeAccessor,
            Type type,
            bool? isRequired,
            bool? isEnumerable)
        {
            Guard.ArgumentNotNull(type, nameof(type));

            var isEnumerableType = type.IsEnumerableType(out var clrType);
            clrType = clrType ?? type;
            _valueResolver = GraphValueResolver.GetResolver(clrType);
            if (isEnumerableType && isEnumerable == false)
            {
                throw new GraphException($"Cannot create non-enumerable GraphType based on the type '{type}'");
            }
            
            Type = clrType;
            IsRequired = isRequired??false;
            IsEnumerable = isEnumerable?? isEnumerableType;
            var name = GraphValueResolver.GetGraphTypeName(clrType);
            var requiredFlag = IsRequired ? "!" : "";
            Name = IsEnumerable
                ? $"[{name}]{requiredFlag}"
                : $"{name}{requiredFlag}";

            Fields = new Dictionary<string, GraphField>();
            if (!GraphValueResolver.IsScalar(clrType))
            {
                foreach (var property in Type.GetProperties())
                {
                    var memberAttribute = attributeAccessor.GetAttribute<GraphMemberAttribute>(property, false);
                    if (memberAttribute?.Ignored == true)
                    {
                        continue;
                    }
                    var fieldName = memberAttribute?.Name ?? property.Name;
                    var resolver = memberAttribute?.Resolver == null
                        ? (IGraphResolver)new PropertyResolver(property)
                        : new MethodResolver(Type.GetMethod(memberAttribute.Resolver));

                    var isPropertyEnumerable = property.PropertyType.IsEnumerableType(out var propertyType);
                    propertyType = propertyType ?? property.PropertyType;
                    var isPropertyRequired = memberAttribute?.IsRequired == true;
                    var propertyGraphType = new GraphType(attributeAccessor, propertyType, isPropertyRequired, isPropertyEnumerable);

                    //Arguments
                    var field = new GraphField(fieldName, propertyGraphType, resolver);
                    var argumentAttributes = attributeAccessor.GetAttributes<ArgumentAttribute>(property, false);
                    foreach (var attribute in argumentAttributes)
                    {
                        if (string.IsNullOrWhiteSpace(attribute.Name))
                        {
                            throw new GraphException("Does not specifiy the Name property of the ArgumentAttribute annotated with property member.");
                        }

                        if (attribute.Type == null)
                        {
                            throw new GraphException("Does not specifiy the Type property of the ArgumentAttribute annotated with property member.");
                        }

                        var isEnumerableArgument = attribute.Type.IsEnumerableType(out var argumentType);
                        argumentType = argumentType ?? attribute.Type;
                        var argumentGraphType = new GraphType(attributeAccessor, argumentType, attribute.IsRequired, attribute.GetIsEnumerable() ?? isEnumerableArgument);
                        var argument = new NamedGraphType(attribute.Name, argumentGraphType);
                        field.AddArgument(argument);
                    }
                    Fields.Add(field.Name, field);
                }
            }
        }
        public bool Equals(GraphType other)
        {
            return other != null && other.Name == Name;
        }
        public override int GetHashCode() => Name.GetHashCode();
        internal static GraphType CreateGraphType(OperationType operationType) => new GraphType(operationType);       
        public object Resolve(object rawValue) => _valueResolver(rawValue);
    }
}
