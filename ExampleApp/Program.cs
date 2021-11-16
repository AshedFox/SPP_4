using System;
using System.Collections.Generic;
using TestsGeneratorLib;

namespace ExampleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            var config = new AnalyzerConfig(3, 6, 3, 
                new List<string>()
                {
                    @"D:\projects\BlogWebApp\BlogWebApp\Controllers\AccountController.cs",
                    @"D:\projects\BlogWebApp\BlogWebApp\Controllers\PostsController.cs",
                    @"D:\projects\BlogWebApp\BlogWebApp\Controllers\FilesController.cs",
                    @"D:\univer\сем_5\СПП\SPP_3\AssemblyBrowserLib\AssemblyParser.cs",
                    @"D:\projects\ChatWebApp\ChatWebApp\Data\MessagesRepository.cs",
                    @"D:\projects\BlogWebApp\BlogWebApp\Controllers\UsersController.cs"
                }, "./result");
            
            var analyzer = new Analyzer(config);
            
            analyzer.Analyze().Wait();
        }
    }
}