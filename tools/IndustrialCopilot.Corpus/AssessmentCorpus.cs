using System.Security.Cryptography;
using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
namespace IndustrialCopilot.Corpus;

public sealed record CorpusDocument(DocumentProcessingRequest Request, string FileName, byte[] Bytes);
/// <summary>Entirely fictional assessment material, not manufacturer guidance or permission to work.</summary>
public static class AssessmentCorpus
{
    private static readonly (string Name, string Component, string Symptom, string Check)[] Families =
    [
        ("pump", "mechanical seal", "seal leakage", "compare the drip tray observation with the isolated seal housing"),
        ("motor", "terminal enclosure", "overheating", "compare recorded casing temperature with the unloaded baseline"),
        ("compressor", "inlet filter", "pressure loss", "inspect the isolated filter for restriction before restarting"),
        ("conveyor", "belt tracking roller", "belt drift", "inspect roller alignment with the belt stationary"),
        ("bearing", "bearing housing", "vibration", "compare the vibration trend with the previous inspection"),
        ("valve", "actuator coupling", "incomplete travel", "inspect the depressurized coupling position"),
        ("gearbox", "oil sight glass", "lubricant foaming", "record oil appearance after safe isolation and settling"),
        ("fan", "impeller guard", "unusual noise", "inspect the stationary impeller for contact marks"),
        ("hydraulic unit", "return filter", "slow cylinder motion", "compare the isolated filter condition with maintenance records"),
        ("cooling loop", "heat exchanger", "high return temperature", "inspect the isolated exchanger for visible fouling")
    ];
    public static IReadOnlyList<CorpusDocument> Generate()
    {
        var result = new List<CorpusDocument>();
        for (var family = 0; family < Families.Length; family++)
        for (var variant = 0; variant < 3; variant++)
        {
            var f = Families[family];
            var procedure = new[] { "fault isolation", "preventive inspection", "return to service review" }[variant];
            var slug = $"manual-{family + 1:00}-{variant + 1}";
            var title = $"Synthetic {f.Name}: {procedure}";
            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            string[][] sections =
            [
                ["Scope and asset identification", $"This fictional {f.Name} procedure covers {procedure}.",
                 $"The component in scope is the {f.Component}. The reported symptom is {f.Symptom}.",
                 "Record the asset label, procedure revision and operating context before planning work.",
                 "Use only the matching manual revision. A similar asset name is not evidence of applicability.",
                 "Compare historical observations with the current symptom; do not invent missing readings.",
                 "This document provides a training scenario, not certified limits or manufacturer instructions."],
                ["Hazards and isolation", $"Before touching the {f.Component}, identify all stored and supplied energy.",
                 "A qualified person must establish electrical isolation and verify absence of hazardous energy.",
                 "Consider pressure, gravity and rotating parts. Apply the site isolation procedure.",
                 "Record each mandatory prerequisite separately; a model proposal cannot mark it satisfied.",
                 "Missing isolation evidence blocks execution. Stop and request competent supervision.",
                 "Human approval does not substitute for physical safety verification."],
                ["Grounded diagnostic observations", $"For {f.Symptom}, {f.Check}.",
                 $"Inspect the {f.Component} only at an approved safe boundary; do not run invasive live tests.",
                 "Record what was observed, the instrument identity and any uncertainty without guessing.",
                 "If the observation does not match this procedure, report insufficient evidence.",
                 "Retain the page and section citation when proposing each diagnostic instruction.",
                 "A changed symptom requires review of applicability before generating an action list."],
                ["Maintenance proposal and review", $"Prepare a {procedure} proposal for the {f.Name} asset.",
                 $"Describe the observed {f.Symptom} and the affected {f.Component}.",
                 "Keep diagnostic instructions distinct from executable repair actions.",
                 "List the final scope and mandatory prerequisites for supervisor review.",
                 "Edits invalidate stale approval. Bind the decision to the exact reviewed revision.",
                 "A rejected proposal must not be dispatched; unresolved hazards must remain blocked."],
                ["Verification and handover", "Confirm that every mandatory safety check has trusted recorded verification.",
                 "Confirm that current supervisor approval covers the final executable scope.",
                 "Dispatch once using the stable request identity; an uncertain response requires reconciliation.",
                 "Record the outcome and supporting source location without claiming unobserved repair success.",
                 $"For recurring {f.Symptom}, escalate to an authorized specialist rather than repeating blindly.",
                 "This synthetic training procedure does not authorize operation of a real industrial asset."]
            ];
            for (var i = 0; i < sections.Length; i++)
            {
                var page = builder.AddPage(595, 842);
                var y = 790d;
                void Line(string text, double size = 11) { page.AddText(text, size, new PdfPoint(42, y), font); y -= 20; }
                Line("SYNTHETIC ASSESSMENT MATERIAL - NOT FOR FIELD USE", 12);
                Line(title, 13); Line($"Revision 1 | {slug} | Page {i + 1} of 5", 10); y -= 12;
                Line($"Section {i + 1}: {sections[i][0]}", 13); y -= 8;
                for (var j = 1; j < sections[i].Length; j++)
                {
                    var words = sections[i][j].Split(' '); var line = $"{i + 1}.{j} ";
                    foreach (var word in words) { if ((line + word).Length > 80) { Line(line); line = "    "; } line += word + " "; }
                    Line(line); y -= 8;
                }
                page.AddText("Source: repository-generated fictional industrial training corpus. No real manufacturer.", 9, new PdfPoint(42, 40), font);
            }
            result.Add(new(new(Id(slug), Id(slug + "-revision-1"), "application/pdf", new(title, "synthetic:" + slug, 1)), slug + ".pdf", builder.Build()));
        }
        result.Add(new(new(Id("text-isolation"), Id("text-isolation-r1"), "text/plain", new("Synthetic isolation handover", "synthetic:text-isolation", 1)), "isolation.txt",
            Encoding.UTF8.GetBytes("SYNTHETIC ASSESSMENT MATERIAL - NOT FOR FIELD USE\nSection 1: Isolation handover\nRecord electrical isolation verification. Supervisor approval and physical verification are separate mandatory gates. Do not dispatch an unverified work order.\n")));
        return result.AsReadOnly();
    }
    private static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes("fr1-v1:" + key)).AsSpan(0, 16));
    public static (int Documents, int Pages) Validate(IReadOnlyList<CorpusDocument> documents)
    {
        if (documents.Select(d => d.Request.DocumentId).Distinct().Count() != documents.Count) throw new InvalidOperationException("Duplicate corpus identity.");
        var pages = 0;
        foreach (var item in documents.Where(d => d.Request.MediaType == "application/pdf"))
        {
            using var pdf = PdfDocument.Open(item.Bytes);
            foreach (var page in pdf.GetPages())
                if (string.IsNullOrWhiteSpace(page.Text)) throw new InvalidOperationException("Corpus contains an empty page.");
            pages += pdf.NumberOfPages;
        }
        if (documents.Count < 30 || pages < 150) throw new InvalidOperationException("Assessment requires at least 30 documents and 150 actual pages.");
        return (documents.Count, pages);
    }
}
