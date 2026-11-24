namespace RaizesDoNordeste.Domain.Comum;

/// <summary>
/// Base para entidades com identidade própria (Guid gerado no domínio,
/// o que permite referenciar a entidade antes de persistir, ex.: em eventos).
/// </summary>
public abstract class Entidade
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
}

/// <summary>
/// Raiz de agregado: além da identidade, acumula eventos de domínio que serão
/// gravados na Outbox na mesma transação do SaveChanges.
/// </summary>
public abstract class RaizAgregado : Entidade
{
    private readonly List<IEventoDominio> _eventos = [];

    public IReadOnlyCollection<IEventoDominio> Eventos => _eventos;

    protected void AdicionarEvento(IEventoDominio evento) => _eventos.Add(evento);

    public void LimparEventos() => _eventos.Clear();
}

public interface IEventoDominio
{
    DateTimeOffset OcorridoEm { get; }
}
