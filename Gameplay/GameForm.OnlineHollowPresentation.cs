namespace Dust;

internal sealed partial class GameForm
{
    private PointF HollowRenderCell(Hollow hollow) =>
        IsOnlineGameplayActive && !IsOnlineSimulationHost &&
        hollow.PresentationReady
            ? hollow.PresentationCell
            : hollow.VisualCell;

    private float HollowRenderFacing(Hollow hollow) =>
        IsOnlineGameplayActive && !IsOnlineSimulationHost &&
        hollow.PresentationReady
            ? hollow.PresentationFacingAngle
            : hollow.FacingAngle;

    private PointF TriangleMemberRenderCell(TriangleMember member) =>
        IsOnlineGameplayActive && !IsOnlineSimulationHost &&
        member.PresentationReady
            ? member.PresentationCell
            : member.VisualCell;

    private void RetargetOnlineHollowPresentation(Hollow hollow)
    {
        var separationX = hollow.PresentationCell.X - hollow.VisualCell.X;
        var separationY = hollow.PresentationCell.Y - hollow.VisualCell.Y;
        var needsHardCorrection =
            !hollow.PresentationReady ||
            hollow.TeleportFlash > 0 ||
            separationX * separationX + separationY * separationY > 2.25f;
        if (needsHardCorrection)
        {
            hollow.PresentationReady = true;
            hollow.PresentationCell = hollow.VisualCell;
            hollow.PreviousPresentationCell = hollow.VisualCell;
            hollow.PresentationFacingAngle = hollow.FacingAngle;
        }
        foreach (var member in hollow.TriangleMembers)
            RetargetOnlineTriangleMemberPresentation(member);
        hollow.PresentationSnapshotAge = 0;
    }

    private static void RetargetOnlineTriangleMemberPresentation(
        TriangleMember member)
    {
        var dx = member.PresentationCell.X - member.VisualCell.X;
        var dy = member.PresentationCell.Y - member.VisualCell.Y;
        if (!member.PresentationReady || dx * dx + dy * dy > 2.25f)
        {
            member.PresentationReady = true;
            member.PresentationCell = member.VisualCell;
            member.PreviousPresentationCell = member.VisualCell;
        }
        member.PresentationSnapshotAge = 0;
    }

    private void UpdateOnlineHollowPresentation(float deltaTime)
    {
        if (!IsOnlineGameplayActive || IsOnlineSimulationHost) return;
        foreach (var hollow in _hollows)
        {
            if (!hollow.PresentationReady)
            {
                RetargetOnlineHollowPresentation(hollow);
                continue;
            }

            hollow.PreviousPresentationCell = hollow.PresentationCell;
            hollow.PresentationSnapshotAge = Math.Min(
                OnlineSnapshotInterval * 2.75f,
                hollow.PresentationSnapshotAge + deltaTime);

            var duration = Math.Max(.08f, hollow.MoveDuration);
            var predictedProgress = Math.Clamp(
                hollow.MoveProgress + hollow.PresentationSnapshotAge / duration,
                0,
                1);
            var segmentFrom = hollow.MoveFrom;
            var segmentTo = hollow.MoveTo;
            var segmentX = segmentTo.X - segmentFrom.X;
            var segmentY = segmentTo.Y - segmentFrom.Y;
            var segmentLengthSquared =
                segmentX * segmentX + segmentY * segmentY;
            var desired = hollow.VisualCell;
            var baseSpeed = 1f / duration;

            if (segmentLengthSquared > .0001f)
            {
                desired = new PointF(
                    segmentFrom.X + segmentX * predictedProgress,
                    segmentFrom.Y + segmentY * predictedProgress);

                // At a turn, finish the old segment by travelling to the new
                // segment's origin before following its prediction. This keeps
                // a smoothed Hollow inside corridors instead of cutting across
                // the corner of a wall.
                var relativeX = hollow.PresentationCell.X - segmentFrom.X;
                var relativeY = hollow.PresentationCell.Y - segmentFrom.Y;
                var projection = (relativeX * segmentX + relativeY * segmentY) /
                                 segmentLengthSquared;
                var projected = new PointF(
                    segmentFrom.X + segmentX * projection,
                    segmentFrom.Y + segmentY * projection);
                var offSegmentX = hollow.PresentationCell.X - projected.X;
                var offSegmentY = hollow.PresentationCell.Y - projected.Y;
                var liesOnSegment =
                    projection is >= -.02f and <= 1.02f &&
                    offSegmentX * offSegmentX + offSegmentY * offSegmentY <= .0025f;
                if (!liesOnSegment)
                {
                    desired = segmentFrom;
                }
                else if (projection > predictedProgress &&
                         projection - predictedProgress < .38f)
                {
                    // Arrival jitter can make a newly received host sample a
                    // fraction older than our prediction. Never visibly rewind
                    // along the same corridor for that small correction.
                    desired = hollow.PresentationCell;
                }

                baseSpeed = MathF.Sqrt(segmentLengthSquared) / duration;
            }

            var lagX = desired.X - hollow.PresentationCell.X;
            var lagY = desired.Y - hollow.PresentationCell.Y;
            var lag = MathF.Sqrt(lagX * lagX + lagY * lagY);
            var catchUp = 1.12f + Math.Min(1.25f, lag * 1.8f);
            hollow.PresentationCell = MoveOnlinePresentationTowards(
                hollow.PresentationCell,
                desired,
                baseSpeed * catchUp * deltaTime);

            var predictedFacing = RotateTowards(
                hollow.FacingAngle,
                hollow.DesiredFacingAngle,
                hollow.TurnSpeed * hollow.PresentationSnapshotAge);
            hollow.PresentationFacingAngle = RotateTowards(
                hollow.PresentationFacingAngle,
                predictedFacing,
                Math.Max(5.5f, hollow.TurnSpeed * 1.55f) * deltaTime);

            foreach (var member in hollow.TriangleMembers)
            {
                if (!member.PresentationReady)
                {
                    RetargetOnlineTriangleMemberPresentation(member);
                    continue;
                }
                member.PreviousPresentationCell = member.PresentationCell;
                member.PresentationSnapshotAge = Math.Min(
                    OnlineSnapshotInterval * 2.75f,
                    member.PresentationSnapshotAge + deltaTime);
                var memberDuration = Math.Max(.08f, hollow.MoveDuration);
                var memberProgress = Math.Clamp(
                    member.MoveProgress +
                    member.PresentationSnapshotAge / memberDuration, 0, 1);
                var memberSegmentFrom = member.MoveFrom;
                var memberSegmentTo = member.MoveTo;
                var memberSegmentX = memberSegmentTo.X - memberSegmentFrom.X;
                var memberSegmentY = memberSegmentTo.Y - memberSegmentFrom.Y;
                var memberSegmentLengthSquared =
                    memberSegmentX * memberSegmentX + memberSegmentY * memberSegmentY;
                var desiredMember = member.VisualCell;
                var memberBaseSpeed = 1f / memberDuration;

                if (memberSegmentLengthSquared > .0001f)
                {
                    desiredMember = new PointF(
                        memberSegmentFrom.X + memberSegmentX * memberProgress,
                        memberSegmentFrom.Y + memberSegmentY * memberProgress);

                    // A freshly received turn can leave the presentation on the
                    // previous orthogonal segment. Finish that segment at the new
                    // origin before following the next one so a shard never takes
                    // a diagonal shortcut through the wall at a corridor corner.
                    var relativeX = member.PresentationCell.X - memberSegmentFrom.X;
                    var relativeY = member.PresentationCell.Y - memberSegmentFrom.Y;
                    var projection =
                        (relativeX * memberSegmentX + relativeY * memberSegmentY) /
                        memberSegmentLengthSquared;
                    var projected = new PointF(
                        memberSegmentFrom.X + memberSegmentX * projection,
                        memberSegmentFrom.Y + memberSegmentY * projection);
                    var offSegmentX = member.PresentationCell.X - projected.X;
                    var offSegmentY = member.PresentationCell.Y - projected.Y;
                    var liesOnSegment =
                        projection is >= -.02f and <= 1.02f &&
                        offSegmentX * offSegmentX + offSegmentY * offSegmentY <= .0025f;
                    if (!liesOnSegment)
                    {
                        desiredMember = memberSegmentFrom;
                    }
                    else if (projection > memberProgress &&
                             projection - memberProgress < .38f)
                    {
                        desiredMember = member.PresentationCell;
                    }

                    memberBaseSpeed = MathF.Sqrt(memberSegmentLengthSquared) /
                                      memberDuration;
                }

                var memberLagX = desiredMember.X - member.PresentationCell.X;
                var memberLagY = desiredMember.Y - member.PresentationCell.Y;
                var memberLag = MathF.Sqrt(
                    memberLagX * memberLagX + memberLagY * memberLagY);
                var memberCatchUp = 1.12f + Math.Min(1.25f, memberLag * 1.8f);
                member.PresentationCell = MoveOnlinePresentationTowards(
                    member.PresentationCell,
                    desiredMember,
                    memberBaseSpeed * memberCatchUp * deltaTime);
            }
        }
    }

    private static PointF MoveOnlinePresentationTowards(
        PointF current,
        PointF target,
        float maximumDistance)
    {
        var dx = target.X - current.X;
        var dy = target.Y - current.Y;
        var distanceSquared = dx * dx + dy * dy;
        if (distanceSquared <= maximumDistance * maximumDistance ||
            distanceSquared <= .000001f)
            return target;
        var scale = maximumDistance / MathF.Sqrt(distanceSquared);
        return new PointF(current.X + dx * scale, current.Y + dy * scale);
    }
}
