using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CipherSuiteTest
{
    class Program
    {
        static void Main(string[] args)
        {
            byte[] myCipherSuites = Convert.FromBase64String("AAQABQAvADXAAsAEwAXADMAOwA/AB8AJwArAEcATwBQAMwA5ADIAOAAKwAPADcAIwBIAFgATAAkAFQASAAMACAAUABEA/w==");
            byte[] myCompressionMethods = Convert.FromBase64String("AA==");
            byte[] myExtensions = Convert.FromBase64String("AAsABAMAAQIACgA0ADIADgANABkACwAMABgACQAKABYAFwAIAAYABwAUABUABAAFABIAEwABAAIAAwAPABAAEQ==");

            string[] lines = File.ReadAllLines("all_cipher_list.csv");

            List<string> converted = new List<string>();

            for (int n = 1; n < lines.Length; ++n)
            {
                try
                {
                    string[] splitted = lines[n].Split(',');

                    StringBuilder sb = new StringBuilder();
                    sb.Append("addMyOwn(\"");
                    sb.Append(splitted[2]);
                    sb.Append("\", 0x");
                    sb.Append(splitted[0].Substring(3, 2));
                    sb.Append(splitted[1].Substring(2, 2));
                    sb.Append(");");

                    converted.Add(sb.ToString());
                }
                catch { }
            }

            File.WriteAllLines("converted.txt", converted);

            return;
        }
    }
}
