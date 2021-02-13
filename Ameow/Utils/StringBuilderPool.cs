using Microsoft.Extensions.ObjectPool;
using System.Text;

namespace Ameow.Utils
{
    public static class StringBuilderPool
    {
        private static readonly ObjectPoolProvider objectPoolProvider;
        private static readonly ObjectPool<StringBuilder> objectPool;

        static StringBuilderPool()
        {
            objectPoolProvider = new DefaultObjectPoolProvider();
            objectPool = objectPoolProvider.CreateStringBuilderPool();
        }

        public static StringBuilder Acquire()
        {
            var sb = objectPool.Get();
            sb.Clear();
            return sb;
        }

        public static StringBuilder Acquire(int capacity)
        {
            var sb = objectPool.Get();
            sb.Clear();
            sb.EnsureCapacity(capacity);
            return sb;
        }

        public static void Release(StringBuilder sb)
        {
            objectPool.Return(sb);
        }

        public static string GetStringAndRelease(StringBuilder sb)
        {
            var str = sb.ToString();
            objectPool.Return(sb);
            return str;
        }
    }
}