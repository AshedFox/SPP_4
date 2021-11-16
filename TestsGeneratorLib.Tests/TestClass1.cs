using System;
using System.Threading.Tasks;

namespace TestsGeneratorLib.Tests
{
    public class TestClass1
    {
        public void TestMethod1(int a)
        {
        }

        public Guid TestMethod2(Guid id, object obj) => id;

        public async Task TestMethod3()
        {
            await Task.Run(() => Console.WriteLine(1));
        }
        
        public async Task<Uri> TestMethod4(Uri uri)
        {
            return await Task.Run(() =>  uri);
        }
    }
}