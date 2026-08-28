using System.Globalization;
using System.Text;
using System.Xml;

namespace Nodal.Import.Relational;

/// <summary>Lists the visualization-oriented export formats provided by the free relational import package.</summary>
public enum RelationalInteractionExportFormat
{
    /// <summary>GraphML for general graph tooling.</summary>
    GraphMl,

    /// <summary>GEXF for Gephi-compatible visualization workflows.</summary>
    Gexf,

    /// <summary>Graphviz DOT for diagrams and technical documentation.</summary>
    Dot,
}

/// <summary>Writes visualization-oriented projections of a canonical relational interaction model.</summary>
public static class RelationalInteractionModelExporter
{
    /// <summary>
    /// Writes an interaction model without changing the physical evidence retained by the canonical JSON model.
    /// </summary>
    /// <param name="model">Model to export.</param>
    /// <param name="format">Target visualization format.</param>
    /// <param name="writer">Caller-owned destination writer.</param>
    public static void Write(
        RelationalInteractionModel model,
        RelationalInteractionExportFormat format,
        TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(writer);
        switch (format)
        {
            case RelationalInteractionExportFormat.GraphMl:
                WriteGraphMl(model, writer);
                break;
            case RelationalInteractionExportFormat.Gexf:
                WriteGexf(model, writer);
                break;
            case RelationalInteractionExportFormat.Dot:
                WriteDot(model, writer);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown relational interaction export format.");
        }
    }

    private static void WriteGraphMl(RelationalInteractionModel model, TextWriter destination)
    {
        using var writer = XmlWriter.Create(destination, XmlSettings());
        writer.WriteStartDocument();
        writer.WriteStartElement("graphml", "http://graphml.graphdrawing.org/xmlns");
        WriteGraphMlKey(writer, "nodeLabel", "node", "label");
        WriteGraphMlKey(writer, "nodeRole", "node", "role");
        WriteGraphMlKey(writer, "edgeLabel", "edge", "label");
        WriteGraphMlKey(writer, "constraint", "edge", "constraint");
        writer.WriteStartElement("graph");
        writer.WriteAttributeString("id", "relational-interaction-network");
        writer.WriteAttributeString("edgedefault", "directed");
        foreach (var item in model.Objects)
        {
            writer.WriteStartElement("node");
            writer.WriteAttributeString("id", item.Id);
            WriteData(writer, "nodeLabel", item.Name);
            WriteData(writer, "nodeRole", item.Role.ToString());
            writer.WriteEndElement();
        }

        foreach (var relation in model.Relations)
        {
            writer.WriteStartElement("edge");
            writer.WriteAttributeString("id", relation.Id);
            writer.WriteAttributeString("source", relation.Display.SourceObjectId);
            writer.WriteAttributeString("target", relation.Display.TargetObjectId);
            WriteData(writer, "edgeLabel", relation.Display.SuggestedLabel);
            WriteData(writer, "constraint", relation.ConstraintName);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteGexf(RelationalInteractionModel model, TextWriter destination)
    {
        using var writer = XmlWriter.Create(destination, XmlSettings());
        writer.WriteStartDocument();
        writer.WriteStartElement("gexf", "http://gexf.net/1.3");
        writer.WriteAttributeString("version", "1.3");
        writer.WriteStartElement("graph");
        writer.WriteAttributeString("mode", "static");
        writer.WriteAttributeString("defaultedgetype", "directed");
        writer.WriteStartElement("nodes");
        foreach (var item in model.Objects)
        {
            writer.WriteStartElement("node");
            writer.WriteAttributeString("id", item.Id);
            writer.WriteAttributeString("label", $"{item.Name} [{item.Role}]");
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteStartElement("edges");
        for (var index = 0; index < model.Relations.Count; index++)
        {
            var relation = model.Relations[index];
            writer.WriteStartElement("edge");
            writer.WriteAttributeString("id", index.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("source", relation.Display.SourceObjectId);
            writer.WriteAttributeString("target", relation.Display.TargetObjectId);
            writer.WriteAttributeString("label", relation.Display.SuggestedLabel);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WriteDot(RelationalInteractionModel model, TextWriter writer)
    {
        writer.WriteLine("digraph RelationalInteractionNetwork {");
        writer.WriteLine("  rankdir=LR;");
        foreach (var item in model.Objects)
        {
            writer.Write("  \"");
            writer.Write(EscapeDot(item.Id));
            writer.Write("\" [label=\"");
            writer.Write(EscapeDot($"{item.Name}\\n[{item.Role}]"));
            writer.WriteLine("\"];");
        }

        foreach (var relation in model.Relations)
        {
            writer.Write("  \"");
            writer.Write(EscapeDot(relation.Display.SourceObjectId));
            writer.Write("\" -> \"");
            writer.Write(EscapeDot(relation.Display.TargetObjectId));
            writer.Write("\" [label=\"");
            writer.Write(EscapeDot(relation.Display.SuggestedLabel));
            writer.WriteLine("\"];");
        }

        writer.WriteLine("}");
    }

    private static XmlWriterSettings XmlSettings() => new()
    {
        CloseOutput = false,
        Indent = true,
        OmitXmlDeclaration = false,
    };

    private static void WriteGraphMlKey(XmlWriter writer, string id, string target, string name)
    {
        writer.WriteStartElement("key");
        writer.WriteAttributeString("id", id);
        writer.WriteAttributeString("for", target);
        writer.WriteAttributeString("attr.name", name);
        writer.WriteAttributeString("attr.type", "string");
        writer.WriteEndElement();
    }

    private static void WriteData(XmlWriter writer, string key, string value)
    {
        writer.WriteStartElement("data");
        writer.WriteAttributeString("key", key);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static string EscapeDot(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("\r", "", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal);
}
