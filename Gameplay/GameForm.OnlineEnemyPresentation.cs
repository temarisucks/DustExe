namespace Dust;

internal sealed partial class GameForm
{
    private float SentryRenderFacing(Sentry sentry) =>
        IsOnlineGameplayActive && !IsOnlineSimulationHost &&
        sentry.PresentationReady
            ? sentry.PresentationFacingAngle
            : sentry.FacingAngle;

    private PointF EnemyProjectileRenderPosition(SentryProjectile projectile) =>
        IsOnlineGameplayActive && !IsOnlineSimulationHost &&
        projectile.PresentationReady
            ? projectile.PresentationPosition
            : projectile.Position;

    private void RetargetOnlineSentryPresentation(Sentry sentry)
    {
        if (!sentry.PresentationReady)
        {
            sentry.PresentationReady = true;
            sentry.PresentationFacingAngle = sentry.FacingAngle;
        }
        sentry.PresentationSnapshotAge = 0;
    }

    private void RetargetOnlineProjectilePresentation(SentryProjectile projectile)
    {
        var dx = projectile.PresentationPosition.X - projectile.Position.X;
        var dy = projectile.PresentationPosition.Y - projectile.Position.Y;
        if (!projectile.PresentationReady || dx * dx + dy * dy > 1.25f)
        {
            projectile.PresentationReady = true;
            projectile.PresentationPosition = projectile.Position;
            projectile.PreviousPresentationPosition = projectile.Position;
        }
        projectile.PresentationSnapshotAge = 0;
    }

    private void UpdateOnlineEnemyProjectilePresentation(float deltaTime)
    {
        if (!IsOnlineGameplayActive || IsOnlineSimulationHost) return;

        foreach (var sentry in _sentries)
        {
            if (!sentry.PresentationReady)
            {
                RetargetOnlineSentryPresentation(sentry);
                continue;
            }
            sentry.PresentationSnapshotAge = Math.Min(
                OnlineSnapshotInterval * 2.75f,
                sentry.PresentationSnapshotAge + deltaTime);
            var predictedFacing = sentry.FacingAngle;
            if (sentry.Phase == SentryPhase.Scanning)
                predictedFacing = NormalizeAngle(
                    predictedFacing + sentry.RotationDirection *
                    SentryTurnSpeed * RunAggressionScale *
                    sentry.PresentationSnapshotAge);
            sentry.PresentationFacingAngle = RotateTowards(
                sentry.PresentationFacingAngle,
                predictedFacing,
                Math.Max(3.2f, SentryTurnSpeed * RunAggressionScale * 1.7f) *
                deltaTime);
        }

        foreach (var projectile in _sentryProjectiles)
        {
            if (!projectile.PresentationReady)
            {
                RetargetOnlineProjectilePresentation(projectile);
                continue;
            }
            projectile.PreviousPresentationPosition = projectile.PresentationPosition;
            projectile.PresentationSnapshotAge = Math.Min(
                OnlineSnapshotInterval * 2.25f,
                projectile.PresentationSnapshotAge + deltaTime);
            var predictionAge = Math.Min(
                projectile.Lifetime,
                projectile.PresentationSnapshotAge);
            var desired = new PointF(
                projectile.Position.X + projectile.Velocity.X * predictionAge,
                projectile.Position.Y + projectile.Velocity.Y * predictionAge);
            var speed = MathF.Sqrt(
                projectile.Velocity.X * projectile.Velocity.X +
                projectile.Velocity.Y * projectile.Velocity.Y);
            projectile.PresentationPosition = MoveOnlinePresentationTowards(
                projectile.PresentationPosition,
                desired,
                speed * 1.35f * deltaTime);
        }
    }
}
