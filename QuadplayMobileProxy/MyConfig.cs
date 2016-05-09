using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace QuadplayMobileProxy
{
    [XmlType("MyConfig")]
    public class MyConfig
    {
        static XmlSerializer serializer = new XmlSerializer(typeof(MyConfig));

        public string NightTransferStart { get; set; }
        public string NightTransferEnd { get; set; }

        public TimeSpan TimeNightTransferStart
        {
            get
            {
                var split = NightTransferStart.Split(':');
                return new TimeSpan(int.Parse(split[0]), int.Parse(split[1]), 0);
            }
        }

        public TimeSpan TimeNightTransferEnd
        {
            get
            {
                var split = NightTransferEnd.Split(':');
                return new TimeSpan(int.Parse(split[0]), int.Parse(split[1]), 0);
            }
        }

        public static MyConfig Load()
        {
            using (var stream = new FileStream("MyConfig.xml", FileMode.Open))
            {
                return (MyConfig)serializer.Deserialize(stream);
            }
        }
    }
}