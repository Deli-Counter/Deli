using System.IO;
using Atlas;
using Valve.Newtonsoft.Json;

namespace Deli
{
    public class JsonAssetReader<T> : IAssetReader<T>
    {
        private IServiceResolver _services;

        public JsonAssetReader(IServiceResolver services)
        {
            _services = services;
        }

        public T ReadAsset(byte[] raw)
        {
            var serializer = _services.Get<JsonSerializer>().Expect("JSON serializer not found");

            using (var memory = new MemoryStream(raw))
            using (var text = new StreamReader(memory))
            using (var json = new JsonTextReader(text))
            {
                return serializer.Deserialize<T>(json);
            }
        }
    }
}