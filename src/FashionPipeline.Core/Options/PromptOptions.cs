using System.Collections.Generic;

namespace FashionPipeline.Core.Options;

public class PromptOptions
{
    public const string SectionName = "Prompts";
    
    // A2A Horizontal Agent Instructions
    public string OrchestratorAgentPrompt { get; set; } = string.Empty;
    public string VisionAgentPrompt { get; set; } = string.Empty;
    public string CreativeAgentPrompt { get; set; } = string.Empty;
    public string MediaAgentPrompt { get; set; } = string.Empty;
    
    // Advanced prompt templates for generating images
    public List<string> ImageGenerationTemplates { get; set; } = new();
}