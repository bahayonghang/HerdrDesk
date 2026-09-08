using System.Text;
using HerdDesk.Contracts;

namespace HerdDesk.App;

public sealed class HelperInstallViewModel
{
    private readonly IHelperDeploymentService _service;

    public HelperInstallViewModel(IHelperDeploymentService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public HelperDeploymentPhase Phase { get; private set; } = HelperDeploymentPhase.Idle;
    public DeploymentPlan? Plan { get; private set; }
    public DeploymentReceipt? Receipt { get; private set; }
    public string? ErrorCode { get; private set; }
    public string SecurityText { get; private set; } = "";
    public bool CanConfirm => Phase == HelperDeploymentPhase.AwaitingConsent && Plan is not null;
    public bool AlwaysAllow => false;

    public async ValueTask PlanAsync(
        DeviceId device,
        SessionKey session,
        SshDeviceSettings settings,
        CancellationToken cancellationToken = default)
    {
        var result = await _service.PlanAsync(device, session, settings, cancellationToken)
            .ConfigureAwait(false);
        Apply(result);
    }

    public async ValueTask ConfirmAsync(CancellationToken cancellationToken = default)
    {
        if (Plan is null)
        {
            ErrorCode = HelperCodes.ConsentRequired;
            Phase = HelperDeploymentPhase.Failed;
            return;
        }

        var result = await _service.ConfirmAsync(
                new HelperConsentValues(Plan.Device, Plan.Version, Plan.Sha256),
                cancellationToken)
            .ConfigureAwait(false);
        Apply(result);
    }

    public async ValueTask CancelAsync()
    {
        var result = await _service.CancelAsync().ConfigureAwait(false);
        Apply(result);
    }

    public async ValueTask RollbackAsync(HelperReceiptKey key, CancellationToken cancellationToken = default)
    {
        var result = await _service.RollbackAsync(key, cancellationToken).ConfigureAwait(false);
        Apply(result);
    }

    private void Apply(HelperDeploymentResult result)
    {
        Phase = result.Phase;
        Plan = result.Plan;
        Receipt = result.Receipt;
        ErrorCode = result.Code;
        SecurityText = Plan is null ? "" : FormatSecurity(Plan);
    }

    internal static string FormatSecurity(DeploymentPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("device ").Append(plan.DisplayDevice).Append('\n');
        builder.Append("os ").Append(plan.Target.Os).Append('\n');
        builder.Append("arch ").Append(plan.Target.Arch).Append('\n');
        builder.Append("target ").Append(plan.Target.Triple).Append('\n');
        builder.Append("version ").Append(plan.Version).Append('\n');
        builder.Append("hash ").Append(plan.Sha256).Append('\n');
        builder.Append("directory ").Append(plan.PrivateDirectory).Append('\n');
        builder.Append("operations");
        foreach (var operation in plan.Operations)
            builder.Append(' ').Append(operation);
        return builder.ToString();
    }
}
