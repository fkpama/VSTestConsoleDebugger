using Sodiware.IO;

namespace Launcher
{
    public readonly struct Target : IEquatable<Target>
    {
        public Target(ProjectSelectorAction Mode, string TargetPath, Guid? Id = null)
        {
            this.Mode = Mode;
            this.TargetPath = TargetPath;
            this.Id = Id;
        }

        public static readonly Target Empy = new();
        public bool IsEmpty => this.Mode == 0
            && this.TargetPath.IsMissing()
            && this.Id == null;
        public ProjectSelectorAction Mode { get; } = ProjectSelectorAction.Executable;
        public string TargetPath { get; }
        public Guid? Id { get; }

        public override bool Equals(object obj) => obj is Target t && Equals(t);
        public bool Equals(Target other)
        {
            if (this.IsEmpty || other.IsEmpty)
                return false;
            if (other.Mode != this.Mode)
                return false;

            if (this.Mode == ProjectSelectorAction.Executable)
            {
                return PathUtils.IsSamePath(this.TargetPath, other.TargetPath);
            }

            return this.Id == other.Id;
        }

        public override int GetHashCode()
        {
            var constant = 138475;
            if (this.Mode == ProjectSelectorAction.Executable)
            {
                if (this.TargetPath.IsMissing())
                    return 0;
                return unchecked(constant + Path.GetFullPath(TargetPath).GetHashCode());
            }
            if (!this.Id.HasValue)
                return 0;

            return unchecked(constant - 4775 + this.Id.Value.GetHashCode());
        }

        public static implicit operator (ProjectSelectorAction Mode, string TargetPath, Guid? Id)(Target value)
        {
            return (value.Mode, value.TargetPath, value.Id);
        }

        public static implicit operator Target((ProjectSelectorAction Mode, string TargetPath, Guid? Id) value)
        {
            return new Target(value.Mode, value.TargetPath, value.Id);
        }

        public static bool operator !=(Target target1, Target target2)
            => !(target1 == target2);
        public static bool operator ==(Target target1, Target target2)
        {
            return target1.Equals(target2);
        }
    }
}
