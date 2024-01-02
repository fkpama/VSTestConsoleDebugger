using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;

namespace Launcher
{
    internal static class Extensions
    {
        readonly struct JSonHelper
        {
            private readonly JsonArray? array;
            private readonly JsonNode? target;
            private readonly JObject? newtonTarget;
            private readonly JArray? newtonArray;

            public bool IsObject
            {
                get => target is not null || newtonTarget is not null;
            }

            public bool IsArray
            {
                get => array is not null || newtonTarget is not null;
            }
            public readonly bool IsValid;

            public JSonHelper(object target)
            {
                this.target = target as JsonNode;
                this.newtonTarget = target as JObject;
                this.array = target as JsonArray;
                this.newtonArray = target as JArray;
                this.IsValid = this.target is not null
                    || this.array is not null
                    || this.newtonTarget is not null
                    || this.newtonArray is not null ;
            }

        }
        private static bool tryGetVal<T>(this ImmutableDictionary<string, object> dictionary, string propertyName, [NotNullWhen(true)]out T? value)
        {
            if (!dictionary.TryGetValue(propertyName, out var strVal))
            {
                value = default;
                return false;
            }

            if (strVal is T t)
            {
                value = t;
                return true;
            }

            var converter = TypeDescriptor.GetConverter(typeof(T));
            if (converter.CanConvertFrom(strVal.GetType()))
            {
                value = (T)converter.ConvertFrom(strVal);
                return true;
            }

            var helper = new JSonHelper(strVal);
            Debug.Assert(helper.IsValid);
            if (!helper.IsValid)
            {
                // TODO: Warning
                throw new NotImplementedException();
            }
            var type = typeof(T);

            if (type.IsArray)
            {
            }

            if (typeof(T).IsArray)
            {
            }

            value = default;
            return value is not null;
        }

        internal static T? GetVal<T>(this ImmutableDictionary<string, object>? dictionary, string propertyName)
        {
            if (dictionary is null) return default;
            return tryGetVal<T>(dictionary, propertyName, out var value) ? value : default;
        }
        internal static string? GetString(this ImmutableDictionary<string, object>? dictionary, string propertyName)
        {
            if (dictionary is null) return null;
            tryGetVal<string>(dictionary, propertyName, out var value);
            return value;
        }
        internal static bool GetBool(this ImmutableDictionary<string, object>? dictionary, string propertyName)
        {
            if (dictionary is null) return false;
            tryGetVal<bool>(dictionary, propertyName, out var value);
            return value;
        }
    }
}
