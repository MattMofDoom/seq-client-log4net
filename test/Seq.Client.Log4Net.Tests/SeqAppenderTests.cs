using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Seq.Client.Log4Net;
using Xunit.Abstractions;
using Exception = System.Exception;

namespace Seq.Client.Log4Net.Tests
{
    public class SeqAppenderTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public SeqAppenderTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public SeqAppender CanConstructAppender()
        {
            return new SeqAppender();
        }

        [Fact]
        public void TestDestructuring()
        {
            Config.MaskType = MaskPolicy.MaskLettersAndNumbers;
            Config.MaskProperties.Add("CustomerNumber");
            Config.DestructureDepth = 1;
            //Config.MaskProperties.Add("UniqueIdentifier");
            Config.PropertyRegex = "((?:\\w+(?!\\s))|(?:\\w+\\s\\w+))\\:\\s(?!\\<)((?:\\w|\\d|\\-)+)(?:\\s|$)";
            string message =
                "Request : UniqueIdentifier: 188a1812-1771-4a1c-a9fa-748b490d7f68 Operation Name: GetAccounts DataElements: <Accounts xmlns=\"\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"> <UniqueIdentifier>828ec9e8-bd99-457a-835f-00045c6056fd</UniqueIdentifier> <StatusCode>0</StatusCode> <CustomerNumber>a123456789</CustomerNumber> <AccountTypeFilter>ALL</AccountTypeFilter> <Items /> </Accounts> Testing test";
            string message2 =
                "<Accounts xmlns=\"\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"> <UniqueIdentifier>828ec9e8-bd99-457a-835f-00045c6056fd</UniqueIdentifier> <StatusCode>0</StatusCode> <CustomerNumber>a123456789</CustomerNumber> <AccountTypeFilter>ALL</AccountTypeFilter> <Items /> </Accounts>";
            string message3 =
                "{\"correlationId\":\"38f2ab03-5a43-4f1f-b9c8-324944cfefe0\",\"message\":\"Started Customer Details\",\"tracePoint\":\"START\",\"priority\":\"INFO\",\"elapsed\":0,\"locationInfo\":{\"lineInFile\":\"50\",\"component\":\"json-logger:logger\",\"fileName\":\"implementation/main-customer-impl.xml\",\"rootContainer\":\"main-customer-impl:\\\\customer-details\"},\"timestamp\":\"2021-07-10T00:00:20.759Z\",\"content\":{\"requestUri\":\"/api/customers/99999999?idType=CIN&cin=99999999\",\"payload\":\"\"},\"applicationName\":\"proc-customer-v2\",\"applicationVersion\":\"2.0\",\"environment\":\"prod\",\"threadName\":\"[MuleRuntime].io.3569: [proc-customer-v2].get:\\\\customers\\\\(id):customer-process-api-config.BLOCKING @162ec5c0\"}";

            _testOutputHelper.WriteLine(processXml(message));
            _testOutputHelper.WriteLine(processXml(message2));
            _testOutputHelper.WriteLine(processJson(message3));
            _testOutputHelper.WriteLine(processRegex(message));
        }

        private string processRegex(string message)
        {
            if (!string.IsNullOrEmpty(Config.PropertyRegex))
                try
                {
                    if (Regex.IsMatch(message, Config.PropertyRegex))
                    {
                        foreach (Match match in Regex.Matches(message, Config.PropertyRegex, RegexOptions.IgnoreCase))
                        {
                            var mask = Masking.Mask(match.Groups[1].Value, match.Groups[2].Value);
                            if (match.Groups[2].Value != mask.ToString())
                                message = message.Replace(match.Groups[2].Value, mask.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    _testOutputHelper.WriteLine(ex.Message);
                    return message;
                }

            return message;
        }

        private string processJson(string message)
        {
            if (message.Contains("{") && message.Contains("}"))
            {
                Dictionary<string, string> jsonValues = new Dictionary<string, string>();
                string possibleJson = message.Substring(message.IndexOf("{", StringComparison.Ordinal),
                    message.LastIndexOf("}", StringComparison.Ordinal) -
                    message.IndexOf("{", StringComparison.Ordinal) + 1);

                var json = new ExpandoObject();
                try
                {
                    json = JsonConvert.DeserializeObject<ExpandoObject>(possibleJson, new JsonSerializerSettings());
                }
                catch (Exception ex)
                {
                    _testOutputHelper.WriteLine("Error: {0}", ex.Message);
                    json = new ExpandoObject();
                }
                
                if (!json.Any()) return message;
                var mask = evaluateJson(0, string.Empty, new MaskJson() {JsonObject = json});
                var outputJson = JsonConvert.SerializeObject(mask.JsonObject);

                if (mask.IsMask)
                {
                    _testOutputHelper.WriteLine("Masking was performed - Properties: {0}", string.Join(",", mask.MaskedProperties.ToArray()));
                    Assert.True(possibleJson != outputJson);
                }
                else
                {
                    _testOutputHelper.WriteLine("Masking was not performed.");
                    Assert.True(possibleJson == outputJson);
                }

                if (!mask.IsMask) return message;
                var s = new StringBuilder();
                s.Append(message.Substring(0, message.IndexOf("{", StringComparison.Ordinal)));
                s.Append(outputJson);
                if (message.Length - 1 > message.LastIndexOf("}", StringComparison.Ordinal))
                    s.Append(message.Substring(message.LastIndexOf("}", StringComparison.Ordinal) + 1,
                        message.Length - message.LastIndexOf("}", StringComparison.Ordinal) - 1));
                message = s.ToString();
            }

            return message;
        }


        private MaskJson evaluateJson(int level = 0, string key = "", MaskJson json = null)
        {
            var cLevel = level + 1;
            var updateJson = new ExpandoObject() as IDictionary<string, object>;
            var maskJson = new MaskJson();
            Config.MaskProperties.Add("correlationId");

            foreach (var x in json.JsonObject)
            {
                if (x.Value.GetType() != typeof(ExpandoObject))
                {
                    var mask = Masking.Mask(x.Key, x.Value);

                    var name = "";
                    if (string.IsNullOrEmpty(key))
                        name = x.Key;
                    else
                        name = key + "_" + x.Key;

                    if (!maskJson.JsonValues.ContainsKey(name) && cLevel <= Config.DestructureDepth)
                    {
                        maskJson.JsonValues.Add(name, mask.ToString());
                    }

                    if (mask != x.Value)
                    {
                        if (!maskJson.MaskedProperties.Contains(x.Key))
                            maskJson.MaskedProperties.Add(x.Key);
                        maskJson.IsMask = true;
                    }

                    updateJson.Add(name, mask.ToString());

                }
                else
                {
                    var subJson = new MaskJson() {JsonObject = (ExpandoObject) x.Value};
                    subJson = string.IsNullOrEmpty(key)
                        ? evaluateJson(cLevel, x.Key, subJson)
                        : evaluateJson(cLevel, key + "_" + x.Key, subJson);

                    if (cLevel <= Config.DestructureDepth)
                    {
                        foreach (var y in subJson.JsonValues)
                        {
                            maskJson.JsonValues.Add(y.Key, y.Value);
                        }

                    }

                    updateJson.Add(x.Key, subJson.JsonObject);
                }

                maskJson.JsonObject = (ExpandoObject) updateJson;

            }

            return maskJson;
        }


        private string processXml(string message)
        {
            if (message.Contains("<") && message.Contains(">"))
            {
                string possibleXml = message.Substring(message.IndexOf("<", StringComparison.Ordinal),
                    message.LastIndexOf(">", StringComparison.Ordinal) -
                    message.IndexOf("<", StringComparison.Ordinal) + 1);

                var xml = new XDocument();
                try
                {
                    xml = XDocument.Parse(possibleXml);
                }
                catch (Exception)
                {
                    xml = new XDocument();
                }

                if (xml.Elements().Any())
                {
                    var mask = evaluateXml(0, string.Empty, new MaskXml() {XmlObject = xml.Root});
                    if (mask.IsMask && xml.Root != null)
                    {
                        _testOutputHelper.WriteLine("Masking was performed - Properties: {0}", string.Join(",", mask.MaskedProperties.ToArray()));
                        xml.Root.ReplaceWith(mask.XmlObject);
                    }
                    else
                    {
                        _testOutputHelper.WriteLine("Masking was not performed");
                    }

                    if (mask.XmlValues.Count > 0 && mask.IsMask)
                    {
                        StringBuilder s = new StringBuilder();
                        s.Append(message.Substring(0, message.IndexOf("<", StringComparison.Ordinal)));
                        s.Append(xml);
                        if (message.Length - 1 > message.LastIndexOf(">", StringComparison.Ordinal))
                            s.Append(message.Substring(message.LastIndexOf(">", StringComparison.Ordinal) + 1,
                                message.Length - message.LastIndexOf(">", StringComparison.Ordinal) - 1));
                        message = s.ToString();
                    }
                }

 
            }

            return message;
        }

        private MaskXml evaluateXml(int level = 0, string key = "", MaskXml xml = null, bool addProperties = true)
        {
            var cLevel = level + 1;
            var updateXml = xml.XmlObject;
            var maskXml = new MaskXml();

            foreach (var x in updateXml.Elements())
            {
                if (!x.HasElements)
                {
                        var mask = Masking.Mask(x.Name.LocalName, x.Value);
                        if (mask.ToString() != x.Value)
                        {
                            maskXml.IsMask = true;
                            if (!maskXml.MaskedProperties.Contains(x.Name.LocalName))
                                maskXml.MaskedProperties.Add(x.Name.LocalName);
                            x.SetValue(mask);
                            updateXml.SetElementValue(x.Name, x.Value);
                        }

                        var name = "";
                        if (string.IsNullOrEmpty(key))
                            name = x.Name.LocalName;
                        else
                            name = key + "_" + x.Name.LocalName;

                        if (!maskXml.XmlValues.ContainsKey(name) && cLevel <= Config.DestructureDepth)
                        {
                            maskXml.XmlValues.Add(name, mask.ToString());
                        }
                }
                else
                {
                    foreach (var y in x.Elements())
                    {
                        var subXml = new MaskXml() {XmlObject = y};
                        subXml = string.IsNullOrEmpty(key)
                            ? evaluateXml(cLevel, y.Name.LocalName, subXml, false)
                            : evaluateXml(cLevel, key + "_" + y.Name.LocalName, subXml, false);

                        if (cLevel <= Config.DestructureDepth)
                        {
                            foreach (var props in subXml.XmlValues)
                                if (!maskXml.XmlValues.ContainsKey(props.Key))
                                    maskXml.XmlValues.Add(props.Key, props.Value);
                        }

                        if (subXml.IsMask)
                        {
                            _testOutputHelper.WriteLine("Setting {0}", y.Name.LocalName);
                            x.SetElementValue(y.Name.LocalName, subXml.XmlObject.Value);
                        }
                    }
                }
            }

            maskXml.XmlObject = updateXml;

            return maskXml;
        }
    }
}
