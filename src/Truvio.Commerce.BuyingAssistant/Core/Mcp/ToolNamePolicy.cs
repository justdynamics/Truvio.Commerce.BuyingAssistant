namespace Truvio.Commerce.BuyingAssistant.Core.Mcp;

/// <summary>
/// Decides which MCP tools the assistant may call. Two gates: the configured allowlist
/// (patterns, trailing * = prefix) and a write guard that blocks tool names that look like
/// mutations unless the operator explicitly allows write tools.
/// </summary>
public static class ToolNamePolicy
{
    private static readonly string[] WritePrefixes =
    {
        "create_", "update_", "delete_", "save_", "patch_", "set_", "assign_", "remove_", "add_", "build_",
        "import_", "upload_", "convert_", "complete_", "end_", "replace_", "reorder_", "flag_", "recalculate_",
        "force_", "validate_", "copy_", "move_", "publish_", "unpublish_", "reset_", "run_", "execute_",
        "start_", "stop_", "clear_", "wait_", "bulk_", "send_", "trigger_", "cancel_", "approve_", "reject_",
        "deserialize_", "serialize_", "install_", "uninstall_", "generate_", "rebuild_", "restart_",
    };

    public static IReadOnlyList<string> ParsePatterns(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(new[] { ',', '\n', '\r', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !p.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool LooksLikeWrite(string toolName)
    {
        var n = toolName.Trim().ToLowerInvariant();
        return WritePrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal));
    }

    public static bool MatchesAny(string toolName, IReadOnlyList<string> patterns)
    {
        foreach (var p in patterns)
        {
            if (p == "*") return true;
            if (p.EndsWith('*'))
            {
                if (toolName.StartsWith(p[..^1], StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (toolName.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Allowed when the allowlist matches and (the tool is not a write, or writes are allowed).</summary>
    public static bool IsAllowed(string toolName, IReadOnlyList<string> allowPatterns, bool allowWriteTools)
    {
        if (allowPatterns.Count == 0) return false;
        if (!MatchesAny(toolName, allowPatterns)) return false;
        return allowWriteTools || !LooksLikeWrite(toolName);
    }
}
