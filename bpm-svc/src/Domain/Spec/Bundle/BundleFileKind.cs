namespace Bpm.Domain.Spec.Bundle;

/// Tags every entry in `manifest.files[]` so loaders can route the bytes
/// to the right reader without relying on path conventions alone. New
/// kinds may be added at the end; renames or reorderings are a schema
/// version bump (see `BundleSchemaVersion`).
public enum BundleFileKind
{
    Manifest,
    Spec,
    BpmnXml,
    SpecMd,
    Readme,
    Walkthrough,
    Changelog,
    Form,
    Notification,
    Sla,
    Actors,
    SampleOrg,
    TestCase,
    Asset,
    Other,
}
