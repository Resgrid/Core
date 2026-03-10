var files = new[]
{
    @"G:\Resgrid\Resgrid\Web\Resgrid.Web\Areas\User\Views\Contacts\Add.cshtml",
    @"G:\Resgrid\Resgrid\Web\Resgrid.Web\Areas\User\Views\Contacts\Edit.cshtml",
    @"G:\Resgrid\Resgrid\Web\Resgrid.Web\Areas\User\Views\Contacts\View.cshtml",
    @"G:\Resgrid\Resgrid\Web\Resgrid.Web\Areas\User\Views\Dispatch\NewCall.cshtml",
    @"G:\Resgrid\Resgrid\Web\Resgrid.Web\Areas\User\Views\Dispatch\UpdateCall.cshtml",
    @"G:\Resgrid\Resgrid\Web\Resgrid.Web\Areas\User\Views\Units\EditUnit.cshtml",
    @"G:\Resgrid\Resgrid\Web\Resgrid.Web\Areas\User\Views\Units\NewUnit.cshtml",
};

foreach (var file in files)
{
    var bytes = System.IO.File.ReadAllBytes(file);
    int start = 0;
    if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        start = 3;
    System.IO.File.WriteAllBytes(file, bytes[start..]);
    Console.WriteLine($"Processed: {file} (start={start})");
}

