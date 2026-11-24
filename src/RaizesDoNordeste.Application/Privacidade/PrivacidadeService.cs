using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RaizesDoNordeste.Application.Abstracoes;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Fidelidade;
using RaizesDoNordeste.Domain.Auditoria;
using RaizesDoNordeste.Domain.Clientes;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.Application.Privacidade;

public sealed record ConsentimentoDto(TipoConsentimento Tipo, bool Concedido, string VersaoTermo, DateTimeOffset RegistradoEm);

public sealed record AlterarConsentimentoRequest(TipoConsentimento Tipo, bool Concedido);

public sealed record DadosTitularDto(
    DateTimeOffset GeradoEm,
    DadosCadastraisDto Cadastro,
    IReadOnlyList<ConsentimentoDto> HistoricoConsentimentos,
    IReadOnlyList<PedidoTitularDto> Pedidos,
    IReadOnlyList<MovimentacaoPontosDto> MovimentacoesPontos);

public sealed record DadosCadastraisDto(Guid Id, string Nome, string Email, DateOnly? DataNascimento, string? Telefone, DateTimeOffset CriadoEm, int SaldoPontos);

public sealed record PedidoTitularDto(Guid Id, Guid UnidadeId, CanalPedido CanalPedido, StatusPedido Status, decimal Total, DateTimeOffset CriadoEm, IReadOnlyList<string> Itens);

public sealed record ClienteAdminDto(Guid Id, string Nome, string Email, DateOnly? DataNascimento, string? Telefone, bool Ativo, bool Anonimizado, int SaldoPontos);

public sealed class AlterarConsentimentoValidator : AbstractValidator<AlterarConsentimentoRequest>
{
    public AlterarConsentimentoValidator() => RuleFor(x => x.Tipo).IsInEnum();
}

/// <summary>
/// Direitos do titular (LGPD art. 18): confirmação e acesso aos dados (exportação), revogação de
/// consentimento e eliminação (implementada como anonimização, pois os pedidos precisam ser
/// mantidos por obrigação legal/fiscal, art. 16, I).
/// </summary>
public sealed class PrivacidadeService(
    IAppDbContext db,
    IUsuarioAtual usuario,
    IAuditoria auditoria,
    ISenhaHasher hasher,
    TimeProvider relogio,
    IOptions<PrivacidadeOpcoes> opcoes,
    IValidator<AlterarConsentimentoRequest> validador)
{
    private static readonly StatusPedido[] EmAndamento =
        [StatusPedido.AguardandoPagamento, StatusPedido.Pago, StatusPedido.EmPreparo, StatusPedido.Pronto];

    public async Task<IReadOnlyList<ConsentimentoDto>> ObterConsentimentosAsync(CancellationToken ct)
    {
        var clienteId = ClienteLogado();
        var todos = await db.Consentimentos.AsNoTracking().Where(c => c.ClienteId == clienteId).ToListAsync(ct);
        return todos
            .GroupBy(c => c.Tipo)
            .Select(g => g.OrderByDescending(c => c.RegistradoEm).First())
            .OrderBy(c => c.Tipo)
            .Select(ParaDto)
            .ToList();
    }

    public async Task<ConsentimentoDto> AlterarConsentimentoAsync(AlterarConsentimentoRequest req, CancellationToken ct)
    {
        await validador.ValidateAndThrowAsync(req, ct);
        var clienteId = ClienteLogado();
        if (req.Tipo == TipoConsentimento.TermosDeUso && !req.Concedido)
            throw new DomainException("TERMOS_OBRIGATORIOS", "Os termos de uso não podem ser revogados isoladamente. Para encerrar o uso, solicite a exclusão da conta.");

        var consentimento = new Consentimento(clienteId, req.Tipo, req.Concedido, opcoes.Value.VersaoTermoAtual, relogio.GetUtcNow(), "api");
        db.Consentimentos.Add(consentimento);
        auditoria.Registrar(AcoesAuditoria.ConsentimentoAlterado, nameof(Cliente), clienteId, new { req.Tipo, req.Concedido });
        await db.SaveChangesAsync(ct);
        return ParaDto(consentimento);
    }

    public async Task<DadosTitularDto> ExportarDadosAsync(CancellationToken ct)
    {
        var clienteId = ClienteLogado();
        var u = await db.Usuarios.AsNoTracking().FirstAsync(x => x.Id == clienteId, ct);
        var c = await db.Clientes.AsNoTracking().FirstAsync(x => x.Id == clienteId, ct);

        var consentimentos = await db.Consentimentos.AsNoTracking().Where(x => x.ClienteId == clienteId)
            .OrderBy(x => x.RegistradoEm).ToListAsync(ct);
        var pedidos = await db.Pedidos.AsNoTracking().Include(p => p.Itens).Where(p => p.ClienteId == clienteId)
            .OrderByDescending(p => p.CriadoEm).ToListAsync(ct);
        var pontos = await db.MovimentacoesPontos.AsNoTracking().Where(m => m.ClienteId == clienteId)
            .OrderByDescending(m => m.OcorridoEm)
            .Select(m => new MovimentacaoPontosDto(m.Tipo, m.Pontos, m.PedidoId, m.OcorridoEm))
            .ToListAsync(ct);

        auditoria.Registrar(AcoesAuditoria.DadosPessoaisExportados, nameof(Cliente), clienteId);
        await db.SaveChangesAsync(ct);

        return new DadosTitularDto(
            relogio.GetUtcNow(),
            new DadosCadastraisDto(u.Id, u.Nome, u.Email, c.DataNascimento, c.Telefone, u.CriadoEm, c.SaldoPontos),
            consentimentos.Select(ParaDto).ToList(),
            pedidos.Select(p => new PedidoTitularDto(p.Id, p.UnidadeId, p.CanalPedido, p.Status, p.Total, p.CriadoEm,
                p.Itens.Select(i => $"{i.Quantidade}x {i.NomeProduto}").ToList())).ToList(),
            pontos);
    }

    public async Task AnonimizarContaAsync(CancellationToken ct)
    {
        var clienteId = ClienteLogado();
        if (await db.Pedidos.AnyAsync(p => p.ClienteId == clienteId && EmAndamento.Contains(p.Status), ct))
            throw new ConflitoException("PEDIDOS_EM_ANDAMENTO", "Há pedidos em andamento. Aguarde a conclusão ou cancele-os antes de excluir a conta.");

        var agora = relogio.GetUtcNow();
        await db.EmTransacaoAsync(async c =>
        {
            var u = await db.Usuarios.FirstAsync(x => x.Id == clienteId, c);
            var cliente = await db.Clientes.FirstAsync(x => x.Id == clienteId, c);
            u.Anonimizar(hasher.Gerar(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")));
            cliente.Anonimizar(agora);

            // Nome para chamada é texto livre e pode identificar a pessoa.
            await db.Pedidos.Where(p => p.ClienteId == clienteId && p.NomeChamada != null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.NomeChamada, (string?)null), c);

            foreach (var tipo in Enum.GetValues<TipoConsentimento>())
                db.Consentimentos.Add(new Consentimento(clienteId, tipo, false, opcoes.Value.VersaoTermoAtual, agora, "anonimizacao"));

            auditoria.Registrar(AcoesAuditoria.ClienteAnonimizado, nameof(Cliente), clienteId, new { Motivo = "Solicitação do titular" });
            await db.SaveChangesAsync(c);
            return true;
        }, ct);
    }

    /// <summary>Consulta administrativa (atendimento ao titular, suporte). Todo acesso fica auditado.</summary>
    public async Task<ClienteAdminDto> ObterClienteComoAdminAsync(Guid clienteId, CancellationToken ct)
    {
        usuario.GarantirPerfil(Perfil.AdminMatriz);
        var dados = await (
            from u in db.Usuarios.AsNoTracking()
            join c in db.Clientes.AsNoTracking() on u.Id equals c.Id
            where u.Id == clienteId
            select new ClienteAdminDto(u.Id, u.Nome, u.Email, c.DataNascimento, c.Telefone, u.Ativo, c.Anonimizado, c.SaldoPontos)
        ).FirstOrDefaultAsync(ct) ?? throw new NaoEncontradoException("Cliente");

        auditoria.Registrar(AcoesAuditoria.DadosPessoaisAcessados, nameof(Cliente), clienteId, new { Finalidade = "Consulta administrativa" });
        await db.SaveChangesAsync(ct);
        return dados;
    }

    private Guid ClienteLogado()
    {
        usuario.GarantirPerfil(Perfil.Cliente);
        return usuario.IdObrigatorio();
    }

    private static ConsentimentoDto ParaDto(Consentimento c) => new(c.Tipo, c.Concedido, c.VersaoTermo, c.RegistradoEm);
}
