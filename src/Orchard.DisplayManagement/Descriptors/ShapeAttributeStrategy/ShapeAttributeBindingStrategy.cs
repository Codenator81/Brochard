﻿using Microsoft.AspNet.Html.Abstractions;
using Microsoft.AspNet.Mvc;
using Microsoft.AspNet.Mvc.Rendering;
using Microsoft.AspNet.Mvc.Routing;
using Microsoft.AspNet.Mvc.ViewFeatures;
using Microsoft.AspNet.Mvc.ViewFeatures.Internal;
using Microsoft.CSharp.RuntimeBinder;
using Microsoft.Extensions.DependencyInjection;
using Orchard.DisplayManagement.Implementation;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Orchard.DisplayManagement.Descriptors.ShapeAttributeStrategy {
    public class ShapeAttributeBindingStrategy : IShapeTableProvider {
        private readonly IEnumerable<ShapeAttributeOccurrence> _shapeAttributeOccurrences;
        private readonly IServiceProvider _componentContext;

        public ShapeAttributeBindingStrategy(
            IEnumerable<ShapeAttributeOccurrence> shapeAttributeOccurrences,
            IServiceProvider componentContext) {
            _shapeAttributeOccurrences = shapeAttributeOccurrences;
            _componentContext = componentContext;
        }

        public void Discover(ShapeTableBuilder builder) {
            foreach (var iter in _shapeAttributeOccurrences) {
                var occurrence = iter;
                var shapeType = occurrence.ShapeAttribute.ShapeType ?? occurrence.MethodInfo.Name;
                builder.Describe(shapeType)
                    .From(occurrence.Feature)
                    .BoundAs(
                        occurrence.MethodInfo.DeclaringType.FullName + "::" + occurrence.MethodInfo.Name,
                        descriptor => CreateDelegate(occurrence, descriptor));
            }
        }

        [DebuggerStepThrough]
        private Func<DisplayContext, IHtmlContent> CreateDelegate(
            ShapeAttributeOccurrence attributeOccurrence,
            ShapeDescriptor descriptor) {
            return context => {
                // oversimplification for the sake of evolving
                return PerformInvoke(context, attributeOccurrence.MethodInfo, _componentContext);
            };
        }

        private IHtmlContent PerformInvoke(DisplayContext displayContext, MethodInfo methodInfo, IServiceProvider serviceInstance) {
            var output = new StringCollectionTextWriter(System.Text.Encoding.UTF8);
            var arguments = methodInfo.GetParameters()
                .Select(parameter => BindParameter(displayContext, parameter, output));

            var returnValue = methodInfo.Invoke(serviceInstance, arguments.ToArray());
            if (methodInfo.ReturnType != typeof(void)) {
                output.Write(CoerceHtmlString(returnValue));
            }
            return output.Content;
        }

        private static IHtmlContent CoerceHtmlString(object invoke) {
            return invoke as IHtmlContent ?? (invoke != null ? new HtmlString(invoke.ToString()) : null);
        }

        private object BindParameter(DisplayContext displayContext, ParameterInfo parameter, TextWriter output) {
            if (parameter.Name == "Shape")
                return displayContext.Value;

            if (parameter.Name == "Display")
                return displayContext.Display;

            if (parameter.Name == "Output" && parameter.ParameterType == typeof(TextWriter))
                return output;

            if (parameter.Name == "Output" && parameter.ParameterType == typeof(Action<object>))
                return new Action<object>(output.Write);

            // meh--
            if (parameter.Name == "Html") {
                return MakeHtmlHelper(displayContext.ViewContext, displayContext.ViewContext.ViewData);
            }

            if (parameter.Name == "Url" && parameter.ParameterType.IsAssignableFrom(typeof(UrlHelper))) {
                return _componentContext.GetService<IUrlHelper>();
            }

            var getter = _getters.GetOrAdd(parameter.Name, n =>
                CallSite<Func<CallSite, object, dynamic>>.Create(
                Microsoft.CSharp.RuntimeBinder.Binder.GetMember(
                CSharpBinderFlags.None, n, null, new[] { CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null) })));

            var result = getter.Target(getter, displayContext.Value);

            if (result == null)
                return null;

            var converter = _converters.GetOrAdd(parameter.ParameterType, CompileConverter);
            var argument = converter.Invoke(result);
            return argument;
        }


        static readonly ConcurrentDictionary<string, CallSite<Func<CallSite, object, dynamic>>> _getters =
            new ConcurrentDictionary<string, CallSite<Func<CallSite, object, dynamic>>>();

        static readonly ConcurrentDictionary<Type, Func<dynamic, object>> _converters =
            new ConcurrentDictionary<Type, Func<dynamic, object>>();

        static Func<dynamic, object> CompileConverter(Type targetType) {
            var valueParameter = Expression.Parameter(typeof(object), "value");

            return Expression.Lambda<Func<object, object>>(
                Expression.Convert(
                    Expression.Property(valueParameter, "value"),
                    typeof(object)),
                valueParameter).Compile();
        }

        private static IHtmlHelper MakeHtmlHelper(ViewContext viewContext, ViewDataDictionary viewData) {
            var newHelper = viewContext.HttpContext.RequestServices.GetRequiredService<IHtmlHelper>();

            var contextable = newHelper as ICanHasViewContext;
            if (contextable != null) {
                var newViewContext = new ViewContext(viewContext, viewContext.View, viewData, viewContext.Writer);
                contextable.Contextualize(newViewContext);
            }

            return newHelper;
        }
    }
}