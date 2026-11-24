using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Identidade;
using RaizesDoNordeste.Application.Pagamentos;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Domain.Pedidos;

namespace RaizesDoNordeste.UnitTests.Aplicacao;

public class AssinaturaWebhookTests
{
    private const string Segredo = "segredo-de-teste-123";
    private static readonly DateTimeOffset Agora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly string Ts = Agora.ToUnixTimeSeconds().ToString();
    private const string Corpo = """{"referencia":"x","status":"aprovado"}""";

    [Fact]
    public void Assinatura_correta_e_aceita()
    {
        var assinatura = AssinaturaWebhook.Calcular(Segredo, Ts, Corpo);
        assinatura.ShouldStartWith("sha256=");
        AssinaturaWebhook.Validar(Segredo, Ts, assinatura, Corpo, Agora, TimeSpan.FromMinutes(5)).ShouldBeTrue();
    }

    [Fact]
    public void Corpo_adulterado_e_rejeitado()
    {
        var assinatura = AssinaturaWebhook.Calcular(Segredo, Ts, Corpo);
        AssinaturaWebhook.Validar(Segredo, Ts, assinatura, Corpo.Replace("aprovado", "recusado"), Agora, TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    [Fact]
    public void Segredo_errado_e_rejeitado() =>
        AssinaturaWebhook.Validar(Segredo, Ts, AssinaturaWebhook.Calcular("outro-segredo", Ts, Corpo), Corpo, Agora, TimeSpan.FromMinutes(5)).ShouldBeFalse();

    [Fact]
    public void Timestamp_antigo_e_rejeitado_replay()
    {
        var antigo = Agora.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        AssinaturaWebhook.Validar(Segredo, antigo, AssinaturaWebhook.Calcular(Segredo, antigo, Corpo), Corpo, Agora, TimeSpan.FromMinutes(5)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, "sha256=abc")]
    [InlineData("123", null)]
    [InlineData("nao-numero", "sha256=abc")]
    public void Headers_ausentes_ou_invalidos_sao_rejeitados(string? ts, string? assinatura) =>
        AssinaturaWebhook.Validar(Segredo, ts, assinatura, Corpo, Agora, TimeSpan.FromMinutes(5)).ShouldBeFalse();
}

public class ValidadoresTests
{
    private readonly CriarPedidoValidator _pedido = new();

    [Fact]
    public void Pedido_valido()
    {
        var r = _pedido.TestValidate(new CriarPedidoRequest(Guid.NewGuid(), [new ItemPedidoRequest(Guid.NewGuid(), 2)], CanalPedido.Totem));
        r.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Pedido_sem_itens_e_invalido() =>
        _pedido.TestValidate(new CriarPedidoRequest(Guid.NewGuid(), [])).ShouldHaveValidationErrorFor(x => x.Itens);

    [Fact]
    public void Pedido_com_produto_repetido_e_invalido()
    {
        var id = Guid.NewGuid();
        _pedido.TestValidate(new CriarPedidoRequest(Guid.NewGuid(), [new(id, 1), new(id, 1)])).ShouldHaveValidationErrorFor(x => x.Itens);
    }

    [Fact]
    public void CanalPedido_e_obrigatorio() =>
        _pedido.TestValidate(new CriarPedidoRequest(Guid.NewGuid(), [new(Guid.NewGuid(), 1)])).ShouldHaveValidationErrorFor(x => x.CanalPedido);

    [Fact]
    public void CanalPedido_fora_do_enum_e_invalido() =>
        _pedido.TestValidate(new CriarPedidoRequest(Guid.NewGuid(), [new(Guid.NewGuid(), 1)], (CanalPedido)99)).ShouldHaveValidationErrorFor(x => x.CanalPedido);

    [Theory]
    [InlineData("curta1")]
    [InlineData("semnumeros")]
    [InlineData("12345678")]
    public void Senha_fraca_e_rejeitada(string senha)
    {
        var v = new RegistrarClienteValidator(new FakeTimeProvider(DateTimeOffset.UtcNow));
        v.TestValidate(new RegistrarClienteRequest("Maria", "maria@x.com", senha, null, null, true, false, false))
            .ShouldHaveValidationErrorFor(x => x.Senha);
    }

    [Fact]
    public void Registro_exige_aceite_dos_termos()
    {
        var v = new RegistrarClienteValidator(new FakeTimeProvider(DateTimeOffset.UtcNow));
        v.TestValidate(new RegistrarClienteRequest("Maria", "maria@x.com", "Senha123", null, null, false, false, false))
            .ShouldHaveValidationErrorFor(x => x.AceiteTermos);
    }

    [Fact]
    public void Registro_de_menor_de_12_anos_e_rejeitado()
    {
        var hoje = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        var v = new RegistrarClienteValidator(new FakeTimeProvider(hoje));
        v.TestValidate(new RegistrarClienteRequest("Joana", "j@x.com", "Senha123", new DateOnly(2016, 1, 1), null, true, false, false))
            .ShouldHaveValidationErrorFor(x => x.DataNascimento);
    }

    [Theory]
    [InlineData(TipoMovimentacaoManual.Ajuste, null)]
    [InlineData(TipoMovimentacaoManual.Perda, "")]
    public void Ajuste_e_perda_exigem_motivo(TipoMovimentacaoManual tipo, string? motivo) =>
        new MovimentarEstoqueValidator().TestValidate(new MovimentarEstoqueRequest(Guid.NewGuid(), tipo, 3, motivo))
            .ShouldHaveValidationErrorFor(x => x.Motivo);

    [Fact]
    public void Perda_com_quantidade_negativa_e_rejeitada() =>
        new MovimentarEstoqueValidator().TestValidate(new MovimentarEstoqueRequest(Guid.NewGuid(), TipoMovimentacaoManual.Perda, -3, "x"))
            .ShouldHaveValidationErrorFor(x => x.Quantidade);
}

public class MascaramentoTests
{
    [Theory]
    [InlineData("maria@exemplo.com", "m***@exemplo.com")]
    [InlineData("a@b.com", "***")]
    [InlineData("invalido", "***")]
    public void Email_e_mascarado_nos_logs(string email, string esperado) =>
        AutenticacaoService.MascararEmail(email).ShouldBe(esperado);
}
