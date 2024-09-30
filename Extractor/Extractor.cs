using System;

namespace Extractor
{
    public abstract class Extractor : IDisposable
    {
        public bool Overwrite { get; set; } = true;

        protected string scsPath;


        public Extractor(string scsPath, bool overwrite) 
        { 
            this.scsPath = scsPath;
            Overwrite = overwrite; 
        }

        public abstract void Extract(string[] startPaths, string destination);

        public abstract void Dispose();
    }
}