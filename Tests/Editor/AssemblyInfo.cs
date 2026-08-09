using System.Runtime.CompilerServices;

// So the Pipeline suite can reuse FakeMetadataProvider rather than duplicating it. The fake stays
// internal on purpose: ContextMetadataRegistry only instantiates types with a public parameterless
// constructor, which is what keeps it out of automatic discovery in a real Editor session.
[assembly: InternalsVisibleTo("PILAR.Context.Pipeline.Tests")]
