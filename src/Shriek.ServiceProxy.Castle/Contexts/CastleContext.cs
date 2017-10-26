﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Castle.DynamicProxy;

namespace Shriek.WebApi.Proxy
{
    /// <summary>
    /// 表示Castle相关上下文
    /// </summary>
    internal class CastleContext
    {
        /// <summary>
        /// 获取HttpHostAttribute
        /// </summary>
        public HttpHostAttribute HostAttribute { get; private set; }

        /// <summary>
        /// 中间路由模版
        /// </summary>
        public RouteAttribute[] RouteAttributes { get; internal set; }

        /// <summary>
        /// 获取ApiReturnAttribute
        /// </summary>
        public ApiReturnAttribute ApiReturnAttribute { get; private set; }

        /// <summary>
        /// 获取ApiActionFilterAttribute
        /// </summary>
        public ApiActionFilterAttribute[] ApiActionFilterAttributes { get; set; }

        /// <summary>
        /// 获取ApiActionDescriptor
        /// </summary>
        public ApiActionDescriptor ApiActionDescriptor { get; private set; }

        /// <summary>
        /// 缓存字典
        /// </summary>
        private static readonly ConcurrentDictionary<IInvocation, CastleContext> cache;

        /// <summary>
        /// Castle相关上下文
        /// </summary>
        static CastleContext()
        {
            CastleContext.cache = new ConcurrentDictionary<IInvocation, CastleContext>(new IInvocationComparer());
        }

        /// <summary>
        /// 从拦截内容获得
        /// 使用缓存
        /// </summary>
        /// <param name="invocation">拦截内容</param>
        /// <returns></returns>
        public static CastleContext From(IInvocation invocation)
        {
            return CastleContext.cache.GetOrAdd(invocation, CastleContext.GetContextNoCache);
        }

        /// <summary>
        /// 从拦截内容获得
        /// </summary>
        /// <param name="invocation">拦截内容</param>
        /// <returns></returns>
        private static CastleContext GetContextNoCache(IInvocation invocation)
        {
            var method = invocation.Method;
            var hostAttribute = CastleContext.GetAttributeFromMethodOrInterface<HttpHostAttribute>(method, false) ??
                                invocation.Proxy?.GetType().GetCustomAttribute<HttpHostAttribute>() ??
                                throw new HttpRequestException("未指定HttpHostAttribute");

            var routeAttributes = CastleContext.GetAttributesFromMethodAndInterface<RouteAttribute>(method, false) ??
                                 new RouteAttribute[] { };

            var returnAttribute = CastleContext.GetAttributeFromMethodOrInterface<ApiReturnAttribute>(method, true);

            var methodFilters = method.GetCustomAttributes<ApiActionFilterAttribute>(true);
            var interfaceFilters = method.DeclaringType.GetCustomAttributes<ApiActionFilterAttribute>(true);
            var filterAttributes = methodFilters.Concat(interfaceFilters).Distinct(new ApiActionFilterAttributeComparer()).ToArray();

            return new CastleContext
            {
                HostAttribute = hostAttribute,
                RouteAttributes = routeAttributes,
                ApiReturnAttribute = returnAttribute,
                ApiActionFilterAttributes = filterAttributes,
                ApiActionDescriptor = CastleContext.GetActionDescriptor(invocation)
            };
        }

        /// <summary>
        /// 生成ApiActionDescriptor
        /// </summary>
        /// <param name="invocation">拦截内容</param>
        /// <returns></returns>
        private static ApiActionDescriptor GetActionDescriptor(IInvocation invocation)
        {
            var method = invocation.Method;
            var descriptor = new ApiActionDescriptor
            {
                Name = method.Name,
                ReturnTaskType = method.ReturnType,
                ReturnDataType = method.ReturnType.IsGenericType ? method.ReturnType.GetGenericArguments().FirstOrDefault() : method.ReturnType,
                Attributes = method.GetCustomAttributes<ApiActionAttribute>(true).ToArray(),
                Parameters = method.GetParameters().Select(GetParameterDescriptor).ToArray()
            };

            return descriptor;
        }

        /// <summary>
        /// 生成ApiParameterDescriptor
        /// </summary>
        /// <param name="parameter">参数信息</param>
        /// <param name="index">参数索引</param>
        /// <returns></returns>
        private static ApiParameterDescriptor GetParameterDescriptor(ParameterInfo parameter, int index)
        {
            var parameterDescriptor = new ApiParameterDescriptor
            {
                Name = parameter.Name,
                Index = index,
                ParameterType = parameter.ParameterType,
                IsSimpleType = IsSimple(parameter.ParameterType),
                IsUriParameterType = IsUriParameterType(parameter.ParameterType),
                Attributes = parameter.GetCustomAttributes<ApiParameterAttribute>(true).ToArray()
            };

            if (typeof(HttpContent).IsAssignableFrom(parameter.ParameterType))
            {
                parameterDescriptor.Attributes = new[] { new HttpContentAttribute() };
            }
            else if (!parameterDescriptor.Attributes.Any())
            {
                parameterDescriptor.Attributes = new[] { new PathQueryAttribute() };
            }
            return parameterDescriptor;
        }

        /// <summary>
        /// 从方法或接口获取特性
        /// </summary>
        /// <typeparam name="TAttribute"></typeparam>
        /// <param name="method">方法</param>
        /// <param name="inherit"></param>
        /// <returns></returns>
        private static TAttribute GetAttributeFromMethodOrInterface<TAttribute>(MethodInfo method, bool inherit) where TAttribute : Attribute
        {
            var attribute = method.GetCustomAttribute<TAttribute>(inherit);
            if (attribute == null)
            {
                attribute = method.DeclaringType.GetCustomAttribute<TAttribute>(inherit);
            }
            return attribute;
        }

        private static TAttribute[] GetAttributesFromMethodAndInterface<TAttribute>(MethodInfo method, bool inherit) where TAttribute : Attribute
        {
            IEnumerable<TAttribute> attributes = new TAttribute[] { };

            //接口优先
            var attribute = method.DeclaringType.GetCustomAttribute<TAttribute>(inherit);
            if (attribute != null) attributes = attributes.Concat(new[] { attribute });

            //第二是方法
            attribute = method.GetCustomAttribute<TAttribute>(inherit);
            if (attribute != null) attributes = attributes.Concat(new[] { attribute });

            return attributes.ToArray();
        }

        /// <summary>
        /// 获取是否为简单类型
        /// </summary>
        /// <param name="type">类型</param>
        /// <returns></returns>
        private static bool IsSimple(Type type)
        {
            if (type.IsGenericType == true)
            {
                type = type.GetGenericArguments().FirstOrDefault();
            }

            if (type.IsPrimitive || type.IsEnum)
            {
                return true;
            }

            return type == typeof(string)
                   || type == typeof(decimal)
                   || type == typeof(DateTime)
                   || type == typeof(Guid)
                   || type == typeof(Uri);
        }

        private static bool IsUriParameterType(Type parameterType)
        {
            return parameterType == typeof(string) ||
                   parameterType == typeof(int) ||
                   parameterType == typeof(int?) ||
                   parameterType == typeof(byte) ||
                   parameterType == typeof(byte?) ||
                   parameterType == typeof(char) ||
                   parameterType == typeof(char?) ||
                   parameterType == typeof(short) ||
                   parameterType == typeof(short?) ||
                   parameterType == typeof(ushort) ||
                   parameterType == typeof(ushort?) ||
                   parameterType == typeof(uint) ||
                   parameterType == typeof(uint?) ||
                   parameterType == typeof(long) ||
                   parameterType == typeof(long?) ||
                   parameterType == typeof(ulong) ||
                   parameterType == typeof(ulong?) ||
                   parameterType == typeof(decimal) ||
                   parameterType == typeof(decimal?) ||
                   parameterType == typeof(float) ||
                   parameterType == typeof(float?) ||
                   parameterType == typeof(double) ||
                   parameterType == typeof(double?) ||
                   parameterType == typeof(DateTime) ||
                   parameterType == typeof(DateTime?) ||
                   parameterType == typeof(Guid) ||
                   parameterType == typeof(Guid?);
        }

        /// <summary>
        /// ApiActionFilterAttribute比较器
        /// </summary>
        private class ApiActionFilterAttributeComparer : IEqualityComparer<ApiActionFilterAttribute>
        {
            /// <summary>
            /// 是否相等
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <returns></returns>
            public bool Equals(ApiActionFilterAttribute x, ApiActionFilterAttribute y)
            {
                return true;
            }

            /// <summary>
            /// 获取哈希码
            /// </summary>
            /// <param name="obj"></param>
            /// <returns></returns>
            public int GetHashCode(ApiActionFilterAttribute obj)
            {
                return obj.TypeId.GetHashCode();
            }
        }

        /// <summary>
        /// IInvocation对象的比较器
        /// </summary>
        private class IInvocationComparer : IEqualityComparer<IInvocation>
        {
            /// <summary>
            /// 是否相等
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <returns></returns>
            public bool Equals(IInvocation x, IInvocation y)
            {
                return x.Proxy.GetHashCode() == y.Proxy.GetHashCode();
            }

            /// <summary>
            /// 获取哈希码
            /// </summary>
            /// <param name="obj"></param>
            /// <returns></returns>
            public int GetHashCode(IInvocation obj)
            {
                return obj.Method.GetHashCode();
            }
        }
    }
}