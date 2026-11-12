namespace ObjectViewer.TestModels;

/// <summary>Standard sample factories for tests and the demo host (PRD §16, §19).</summary>
public static class SampleData
{
    public static IAnimal CreateDog()
    {
        var dog = new Dog();
        var owner = new Owner { Pet = dog };
        dog.Owner = owner;
        return dog;
    }

    public static Order CreateOrder(int itemCount = 12_500)
    {
        var order = new Order
        {
            Customer = new Customer { Address = new Address() },
            Items = Enumerable.Range(0, itemCount)
                .Select(i => new OrderItem { ItemId = i, SerialNumber = $"SN-{i:D8}", Price = 9.9m + i })
                .ToList(),
        };
        order.Parent = order;
        return order;
    }

    public static Department CreateCyclicDepartment()
    {
        var department = new Department();
        var employee = new Employee { Department = department };
        department.Employees.Add(employee);
        return department;
    }

    public static Device CreateFailingDevice() => new();

    public static Credentials CreateCredentials() => new();

    public static Dictionary<string, object> CreateSpecialDictionary() => new()
    {
        ["normal"] = "value",
        ["with'quote"] = 42,
        ["with space"] = true,
        ["你好世界"] = 3.14,
        ["password"] = "super-secret",
    };

    public static LinkedNode CreateDeepChain(int depth = 40)
    {
        var head = new LinkedNode { Value = 0 };
        var current = head;
        for (var i = 1; i < depth; i++)
        {
            current.Next = new LinkedNode { Value = i };
            current = current.Next;
        }
        return head;
    }

    public static int[,] CreateMultiDimensionalArray()
    {
        var array = new int[3, 4];
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 4; j++)
                array[i, j] = i * 10 + j;
        return array;
    }

    public static byte[] CreateBytes(int length = 4_096)
    {
        var bytes = new byte[length];
        var random = new Random(42);
        random.NextBytes(bytes);
        return bytes;
    }

    public static List<object> CreateSharedReferenceGraph()
    {
        var shared = new Address { Street = "Shared Street", City = "Shared City" };
        return
        [
            new Customer { Name = "A", Address = shared },
            new Customer { Name = "B", Address = shared },
        ];
    }
}
