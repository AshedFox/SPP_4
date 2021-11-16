using System.Collections.Generic;

namespace TestsGeneratorLib
{
    public class AnalyzerConfig
    {
        public AnalyzerConfig(int maxFilesReadingParallel, int maxFilesWritingParallel, 
            int maxTestClassesGeneratingParallel, List<string> filesPaths, string savePath)
        {
            MaxFilesReadingParallel = maxFilesReadingParallel;
            MaxFilesWritingParallel = maxFilesWritingParallel;
            MaxTestClassesGeneratingParallel = maxTestClassesGeneratingParallel;
            FilesPaths = filesPaths;
            SavePath = savePath;
        }

        public int MaxFilesReadingParallel { get; set; }
        public int MaxFilesWritingParallel { get; set; }
        public int MaxTestClassesGeneratingParallel { get; set; }
        public List<string> FilesPaths { get; set; }
        public string SavePath { get; set; }
    }
}