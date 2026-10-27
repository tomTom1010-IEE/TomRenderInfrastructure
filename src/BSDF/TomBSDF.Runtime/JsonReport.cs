using System.IO;
using System.Runtime.Serialization.Json;

namespace TomBSDFRuntime
{
    // Player JsonUtility omits composite DTO fields from this external assembly.
    static class JsonReport
    {
        public static void Write<T>(string path, T value)
        {
            using (var stream = File.Create(path))
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }
    }
}
