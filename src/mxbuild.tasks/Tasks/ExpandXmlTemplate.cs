using System.Linq;
using Microsoft.Build.Framework;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Xml;
using System;

namespace Mxbuild.Tasks {
    internal class EmptyItemsException : Exception { }

    public sealed class ExpandXmlTemplate : AbstractTask {
        private const string Name = "__Name";

        private Lookup<string, ITaskItem> m_items;
        private string m_itemName;
        private ITaskItem m_item;
        private Queue<ITaskItem> m_itemQueue;
        private Stack<XElement> m_debug = new Stack<XElement>();

        protected override void Run() {
            m_items = (Lookup<string, ITaskItem>)Items.ToLookup(o => o.GetMetadata(Name), StringComparer.InvariantCultureIgnoreCase);
            var doc = CopyAndExpand(XDocument.Parse(Input));
            Result = doc.Declaration.ToString() + Environment.NewLine + doc.ToString();
        }

        private object CopyAndExpand(XObject node) {
            var type = node.NodeType;

            switch (type) {
                case XmlNodeType.Element:
                    return CopyAndExpand((XElement)node);

                case XmlNodeType.Attribute:
                    return CopyAndExpand((XAttribute)node);

                case XmlNodeType.Document:
                    return CopyAndExpand((XDocument)node);

                case XmlNodeType.Text:
                    return CopyAndExpand((XText)node);

                case XmlNodeType.CDATA:
                    return CopyAndExpand((XCData)node);

                case XmlNodeType.Comment:
                    return CopyAndExpand((XComment)node);

                case XmlNodeType.Whitespace:
                case XmlNodeType.SignificantWhitespace:
                    return node;

                case XmlNodeType.EntityReference:
                case XmlNodeType.Entity:
                case XmlNodeType.ProcessingInstruction:
                case XmlNodeType.DocumentType:
                case XmlNodeType.DocumentFragment:
                case XmlNodeType.Notation:
                case XmlNodeType.XmlDeclaration:
                case XmlNodeType.EndElement:
                case XmlNodeType.EndEntity:
                default:
                    throw new Exception(
                        $"Unexpected node type '{type}' encountered during expansion.");
            }
        }
        private object CopyAndExpand(IEnumerable<XObject> nodes) => nodes.Select(o => CopyAndExpand(o));
        private XDocument CopyAndExpand(XDocument document) => new XDocument(document.Declaration, CopyAndExpand(document.Nodes()));
        private XComment CopyAndExpand(XComment comment) => new XComment(Expand(comment.Value));
        private XText CopyAndExpand(XText text) => new XText(Expand(text.Value));
        private XCData CopyAndExpand(XCData cdata) => new XCData(Expand(cdata.Value));
        private IEnumerable<XObject> CopyAndExpand(XElement element) {
            m_debug.Push(element);

            var isExpanding = m_itemQueue != null;

            while (true) {
                XElement result;

                try {
                    result = new XElement(element.Name,
                        new object[] {
                            CopyAndExpand(element.Attributes()),
                            CopyAndExpand(element.Nodes())
                        }
                    );
                } catch (EmptyItemsException) {
                    break;
                }

                yield return result;

                if (isExpanding || m_itemQueue == null || !m_itemQueue.Any())
                    break;

                m_item = m_itemQueue.Dequeue();
            }

            if (!isExpanding) {
                m_itemQueue = null;
                m_item = null;
                m_itemName = null;
            }

            m_debug.Pop();
        }
        private XAttribute CopyAndExpand(XAttribute attribute) {
            return new XAttribute(attribute.Name, Expand(attribute.Value));
        }
        private string Expand(string value) => Regex.Replace(value, @"%[(](?<first>\w*)([.](?<second>\w*))?[)]", Expand);
        private string Expand(Match match) {
            var first = match.Groups["first"].Value;
            var second = match.Groups["second"].Value;

            // %(metadataName)
            var metadataName = first;

            // %(itemName.metadataName)
            if (!string.IsNullOrEmpty(second)) {
                var itemName = first;
                metadataName = second;

                // first iteration of an item
                if (m_itemName == null) {
                    m_itemName = first;

                    m_itemQueue = new Queue<ITaskItem>();
                    foreach (var o in m_items[itemName])
                        m_itemQueue.Enqueue(o);

                    if (!m_itemQueue.Any())
                        throw new EmptyItemsException();

                    m_item = m_itemQueue.Dequeue();
                } 

                // detected nested item names
                else if (m_itemName != itemName) 
                    throw new Exception(
                        $"While expanding '{m_itemName}' encountered nested expansion '{itemName}'.");
            }

            // substitute variable with metadata
            return m_item.GetMetadata(metadataName);
        }

        [Required]
        public string Input { get; set; }

        [Required]
        public ITaskItem[] Items { get; set; }

        [Output]
        public string Result { get; set; }
    }
}
