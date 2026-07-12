using System;

namespace Klrpxy.Gameplay.Tags.Runtime
{
    public sealed class TagQueryRuntime<TTag>
    {
        private readonly Func<TagSetRuntime<TTag>, bool> matches;

        public TagQueryRuntime(Func<TagSetRuntime<TTag>, bool> matches)
        {
            this.matches = matches;
        }

        public bool Matches(TagSetRuntime<TTag> tags) => matches(tags);

        public static TagQueryRuntime<TTag> All(params TagQueryRuntime<TTag>[] queries)
        {
            TagQueryRuntime<TTag>[] copy = Copy(queries);
            return new TagQueryRuntime<TTag>(tags =>
            {
                foreach (TagQueryRuntime<TTag> query in copy)
                {
                    if (!query.Matches(tags))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        public static TagQueryRuntime<TTag> Any(params TagQueryRuntime<TTag>[] queries)
        {
            TagQueryRuntime<TTag>[] copy = Copy(queries);
            return new TagQueryRuntime<TTag>(tags =>
            {
                foreach (TagQueryRuntime<TTag> query in copy)
                {
                    if (query.Matches(tags))
                    {
                        return true;
                    }
                }

                return false;
            });
        }

        public static TagQueryRuntime<TTag> None(params TagQueryRuntime<TTag>[] queries)
        {
            TagQueryRuntime<TTag>[] copy = Copy(queries);
            return new TagQueryRuntime<TTag>(tags =>
            {
                foreach (TagQueryRuntime<TTag> query in copy)
                {
                    if (query.Matches(tags))
                    {
                        return false;
                    }
                }

                return true;
            });
        }

        private static TagQueryRuntime<TTag>[] Copy(TagQueryRuntime<TTag>[] queries)
        {
            var copy = new TagQueryRuntime<TTag>[queries.Length];
            Array.Copy(queries, copy, queries.Length);
            return copy;
        }
    }
}
