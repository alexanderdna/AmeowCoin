using Newtonsoft.Json;
using System.IO;

namespace Ameow.Storage
{
    /// <summary>
    /// Base class for database files. Provides basic interface for reading and writing JSON representation of them.
    /// </summary>
    public abstract class DbFile
    {
        /// <summary>
        /// Reads and deserialize a database file.
        /// </summary>
        /// <typeparam name="T">Type of the file.</typeparam>
        /// <param name="path">Path to the file.</param>
        /// <returns>The file if it exists. Otherwise null.</returns>
        public static T Read<T>(string path) where T : DbFile
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            var obj = JsonConvert.DeserializeObject<T>(json);
            return obj;
        }

        /// <summary>
        /// Serializes and writes the given database file.
        /// </summary>
        /// <typeparam name="T">Type of the file.</typeparam>
        /// <param name="path">Path to the file.</param>
        /// <param name="file">The file itself.</param>
        public static void Write<T>(string path, T file) where T : DbFile
        {
            var dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir) is false)
                Directory.CreateDirectory(dir);

            var json = JsonConvert.SerializeObject(file);
            File.WriteAllText(path, json);
        }
    }
}