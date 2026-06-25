public class AttributeTests
{
    [Fact]
    public void ScopedAttribute_DefaultConstructor_HasNullServiceType()
    {
        var attr = new ScopedAttribute();
        Assert.Null(attr.ServiceType);
        Assert.Null(attr.Key);
    }

    [Fact]
    public void ScopedAttribute_WithServiceType_SetsProperty()
    {
        var attr = new ScopedAttribute(typeof(IOrderService));
        Assert.Equal(typeof(IOrderService), attr.ServiceType);
    }

    [Fact]
    public void ScopedAttribute_KeyProperty_AcceptsString()
    {
        var attr = new ScopedAttribute { Key = "myKey" };
        Assert.Equal("myKey", attr.Key);
    }

    [Fact]
    public void ScopedAttribute_KeyProperty_AcceptsEnum()
    {
        var attr = new ScopedAttribute { Key = PaymentProvider.Stripe };
        Assert.Equal(PaymentProvider.Stripe, attr.Key);
    }

    [Fact]
    public void SingletonAttribute_DefaultConstructor_HasNullServiceType()
    {
        var attr = new SingletonAttribute();
        Assert.Null(attr.ServiceType);
        Assert.Null(attr.Key);
    }

    [Fact]
    public void SingletonAttribute_WithServiceType_SetsProperty()
    {
        var attr = new SingletonAttribute(typeof(ICache));
        Assert.Equal(typeof(ICache), attr.ServiceType);
    }

    [Fact]
    public void TransientAttribute_DefaultConstructor_HasNullServiceType()
    {
        var attr = new TransientAttribute();
        Assert.Null(attr.ServiceType);
        Assert.Null(attr.Key);
    }

    [Fact]
    public void TransientAttribute_WithServiceType_SetsProperty()
    {
        var attr = new TransientAttribute(typeof(EmailSender));
        Assert.Equal(typeof(EmailSender), attr.ServiceType);
    }

    [Fact]
    public void OptionsAttribute_DefaultConstructor_DerivesSectionFromClassName()
    {
        var attr = new OptionsAttribute();
        Assert.Null(attr.Section); // null means "derive from class name"
        Assert.True(attr.ValidateDataAnnotations);
        Assert.True(attr.ValidateOnStart);
    }

    [Fact]
    public void OptionsAttribute_WithSection_SetsProperty()
    {
        var attr = new OptionsAttribute("MySection");
        Assert.Equal("MySection", attr.Section);
    }
}
