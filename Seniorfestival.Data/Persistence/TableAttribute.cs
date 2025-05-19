public class TableAttribute : Attribute
{
  public string name { get; set; }
  public TableAttribute(string name)
  {
    this.name = name;
  }
}