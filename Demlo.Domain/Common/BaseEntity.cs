namespace Demlo.Domain.Common;


/// An abstract base class that enforces system-wide audit and tracking standards.
/// Enforces Section 4.1 of the Engineering Bible.

public abstract class BaseEntity
{

    /// Unique primary key identifier for the entity record.
    public Guid Id { get; set; } = Guid.NewGuid();

    /// Timestamp when the entity record was created in the database layer.
    public DateTime CreatedAt { get; set; }

    /// Timestamp when the entity record was last updated in the database layer.
    public DateTime? UpdatedAt { get; set; }

    /// Timestamp indicating when a user-facing record was soft-deleted.
    /// A null value indicates the record is active.
    public DateTime? DeletedAt { get; set; }
}
