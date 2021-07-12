using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Seq.Client.Log4Net
{
    public class MaskXml
    {
        public bool IsMask;
        public bool IsCorrelate;
        public List<string> MaskedProperties { get; set; } = new List<string>();
        public Dictionary<string, string> XmlValues { get; set; }= new Dictionary<string, string>();
        public XElement XmlObject { get; set; }
    }

}
