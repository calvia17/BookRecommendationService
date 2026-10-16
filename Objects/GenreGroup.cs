namespace RabbitHole.Vision.Worker.Objects
{
    /// <summary>
    /// The genre group.
    /// This specifies the genre group of a book.
    /// Multiple genres can be part of a single genre group.
    /// It is used to ensure that book across different genre groups are not clustered/recommended.
    /// </summary>
    public enum GenreGroup
    {
        Unknown = 0,
        Romance = 1,
        ScienceFiction = 2,
        Suspense = 3,
        OtherFiction = 4,
        NonFiction = 5
    }
}
