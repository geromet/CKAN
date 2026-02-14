using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CKAN
{
    /// <summary>
    /// [De]serializes a dictionary that might have some questionably
    /// valid data in it.
    /// If exceptions are thrown for any key/value pair, leave it out.
    /// Removes CkanModule objects from AvailableModule.module_version
    /// if License throws BadMetadataKraken.
    /// </summary>
    public class JsonLeakySortedDictionaryConverter<K, V> : JsonConverter
        where K : class
        where V : class
    {
        // Static nested class ensures one compiled constructor per K type
        private static class KeyFactory
        {
            public static readonly Func<string, K> Create = CompileConstructor();
            
            private static Func<string, K> CompileConstructor()
            {
                var ctor = typeof(K).GetConstructor(new[] { typeof(string) });
                if (ctor == null)
                    throw new InvalidOperationException(
                        $"Type {typeof(K)} has no constructor that takes a single string parameter");
                
                var parameter = Expression.Parameter(typeof(string), "key");
                var constructorCall = Expression.New(ctor, parameter);
                var lambda = Expression.Lambda<Func<string, K>>(constructorCall, parameter);
                
                return lambda.Compile();
            }
        }
        
        public override object? ReadJson(JsonReader reader, Type objectType, 
            object? existingValue, JsonSerializer serializer)
        {
            var dict = new SortedDictionary<K, V>();
            var createKey = KeyFactory.Create;  // Get cached compiled constructor
            
            foreach (var kvp in JObject.Load(reader))
            {
                try
                {
                    var k = createKey(kvp.Key);  // ← NO REFLECTION, compiled IL
                    var v = kvp.Value?.ToObject<V>();
                    
                    if (k != null && v != null)
                    {
                        dict.Add(k, v);
                    }
                }
                catch (Exception exc)
                {
                    log.Warn($"Failed to deserialize {kvp.Key}: {kvp.Value}", exc);
                }
            }
            return dict;
        }

        /// <summary>
        /// Use default serializer for writing
        /// </summary>
        [ExcludeFromCodeCoverage]
        public override bool CanWrite => false;

        [ExcludeFromCodeCoverage]
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// We *only* want to be triggered for types that have explicitly
        /// set an attribute in their class saying they can be converted.
        /// By returning false here, we declare we're not interested in participating
        /// in any other conversions.
        /// </summary>
        /// <returns>
        /// false
        /// </returns>
        [ExcludeFromCodeCoverage]
        public override bool CanConvert(Type objectType) => false;

        private static readonly ILog log = LogManager.GetLogger(typeof(JsonLeakySortedDictionaryConverter<K, V>));
    }
}
