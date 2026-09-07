using Mailtide.Core;

namespace Mailtide.Core.Tests;

[TestClass]
public sealed class AttachmentContentTypeTests
{
    [TestMethod]
    public void FromFileName_maps_common_extensions()
    {
        Assert.AreEqual("text/plain", AttachmentContentType.FromFileName("notes.txt"));
        Assert.AreEqual("application/pdf", AttachmentContentType.FromFileName(@"C:\Inbox\Invoice.PDF"));
        Assert.AreEqual("image/jpeg", AttachmentContentType.FromFileName("photo.JPG"));
        Assert.AreEqual("image/png", AttachmentContentType.FromFileName("image.png"));
        Assert.AreEqual(AttachmentContentType.OctetStream, AttachmentContentType.FromFileName("weird.xyz"));
        Assert.AreEqual(AttachmentContentType.OctetStream, AttachmentContentType.FromFileName("noext"));
    }

    [TestMethod]
    public void Resolve_keeps_explicit_types_but_replaces_octet_stream()
    {
        Assert.AreEqual("text/plain", AttachmentContentType.Resolve("notes.txt", "application/octet-stream"));
        Assert.AreEqual("IMAGE/PNG", AttachmentContentType.Resolve("photo.png", "IMAGE/PNG"));
        Assert.AreEqual("text/plain", AttachmentContentType.Resolve("notes.txt", "text/plain"));
        Assert.AreEqual(AttachmentContentType.OctetStream, AttachmentContentType.Resolve("weird.xyz", null));
    }
}
