using RaizesDoNordeste.Domain.Catalogo;
using RaizesDoNordeste.Domain.Comum;
using RaizesDoNordeste.Domain.Estoques;
using RaizesDoNordeste.Domain.Identidade;
using RaizesDoNordeste.Domain.Pagamentos;
using RaizesDoNordeste.Domain.Unidades;

namespace RaizesDoNordeste.UnitTests.Dominio;

public class UnidadeTests
{
    private static Unidade Recife(TimeOnly abre, TimeOnly fecha) =>
        new("Teste", "Recife", "PE", Regiao.Nordeste, TipoUnidade.CozinhaCompleta, "America/Recife", abre, fecha);

    // 12:00 UTC = 09:00 em Recife (UTC-3)
    private static readonly DateTimeOffset MeioDiaUtc = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Aberta_dentro_do_horario_local() => Recife(new(6, 0), new(22, 0)).EstaAberta(MeioDiaUtc).ShouldBeTrue();

    [Fact]
    public void Fechada_fora_do_horario_local() => Recife(new(10, 0), new(22, 0)).EstaAberta(MeioDiaUtc).ShouldBeFalse();

    [Fact]
    public void Abertura_igual_fechamento_significa_24h() => Recife(new(0, 0), new(0, 0)).EstaAberta(MeioDiaUtc).ShouldBeTrue();

    [Fact]
    public void Expediente_que_vira_a_meia_noite()
    {
        var u = Recife(new(18, 0), new(2, 0));
        u.EstaAberta(new DateTimeOffset(2026, 11, 12, 3, 30, 0, TimeSpan.Zero)).ShouldBeTrue();  // 00:30 local
        u.EstaAberta(new DateTimeOffset(2026, 11, 12, 6, 0, 0, TimeSpan.Zero)).ShouldBeFalse();  // 03:00 local
    }

    [Fact]
    public void Unidade_inativa_nunca_esta_aberta()
    {
        var u = Recife(new(0, 0), new(0, 0));
        u.Atualizar(u.Nome, u.Cidade, u.Uf, u.Regiao, u.Tipo, u.FusoHorario, u.HoraAbertura, u.HoraFechamento, ativa: false);
        u.EstaAberta(MeioDiaUtc).ShouldBeFalse();
    }

    [Fact]
    public void Considera_o_fuso_de_cada_unidade()
    {
        // 10:30 UTC = 07:30 em Recife (UTC-3) e 06:30 em Manaus (UTC-4)
        var instante = new DateTimeOffset(2026, 11, 11, 10, 30, 0, TimeSpan.Zero);
        Recife(new(7, 0), new(22, 0)).EstaAberta(instante).ShouldBeTrue();
        new Unidade("M", "Manaus", "AM", Regiao.Norte, TipoUnidade.CozinhaReduzida, "America/Manaus", new(7, 0), new(22, 0))
            .EstaAberta(instante).ShouldBeFalse();
    }

    [Fact]
    public void Fuso_invalido_e_rejeitado() =>
        Should.Throw<DomainException>(() => new Unidade("X", "Y", "PE", Regiao.Nordeste, TipoUnidade.CozinhaCompleta, "Marte/Olympus", new(6, 0), new(22, 0)))
            .Codigo.ShouldBe("FUSO_INVALIDO");
}

public class JanelaSazonalTests
{
    private static readonly JanelaSazonal Junino = new(601, 731);

    [Theory]
    [InlineData(2026, 6, 1, true)]
    [InlineData(2026, 6, 24, true)]
    [InlineData(2026, 7, 31, true)]
    [InlineData(2026, 5, 31, false)]
    [InlineData(2026, 8, 1, false)]
    [InlineData(2026, 11, 11, false)]
    public void Periodo_junino(int a, int m, int d, bool esperado) => Junino.Contem(new DateOnly(a, m, d)).ShouldBe(esperado);

    [Theory]
    [InlineData(12, 15, true)]
    [InlineData(1, 10, true)]
    [InlineData(2, 1, false)]
    public void Janela_que_vira_o_ano(int m, int d, bool esperado) =>
        new JanelaSazonal(1201, 131).Contem(new DateOnly(2026, m, d)).ShouldBe(esperado);

    [Theory]
    [InlineData("06-01", 601)]
    [InlineData("12-31", 1231)]
    [InlineData("02-29", 229)]
    public void Parse_mes_dia(string texto, int mmdd) => JanelaSazonal.ParseMesDia(texto).ShouldBe(mmdd);

    [Theory]
    [InlineData("13-01")]
    [InlineData("02-30")]
    [InlineData("junho")]
    public void Parse_invalido(string texto) =>
        Should.Throw<DomainException>(() => JanelaSazonal.ParseMesDia(texto)).Codigo.ShouldBe("SAZONALIDADE_INVALIDA");

    [Fact]
    public void Produto_sem_sazonalidade_sempre_disponivel() =>
        new Produto("Café", null, CategoriaProduto.Bebida, 5, false, null).DisponivelNaData(new DateOnly(2026, 1, 1)).ShouldBeTrue();

    [Fact]
    public void Produto_que_exige_cozinha_completa_nao_e_compativel_com_unidade_reduzida()
    {
        var cuscuz = new Produto("Cuscuz", null, CategoriaProduto.Cuscuz, 12, requerCozinhaCompleta: true, null);
        cuscuz.CompativelCom(TipoUnidade.CozinhaReduzida).ShouldBeFalse();
        cuscuz.CompativelCom(TipoUnidade.CozinhaCompleta).ShouldBeTrue();
    }

    [Fact]
    public void Preco_local_sobrescreve_preco_base()
    {
        var p = new Produto("Tapioca", null, CategoriaProduto.Tapioca, 18.90m, false, null);
        var item = new ItemCardapio(Guid.NewGuid(), p.Id);
        item.PrecoEfetivo(p).ShouldBe(18.90m);
        item.Configurar(true, 21.50m, "Variação regional");
        item.PrecoEfetivo(p).ShouldBe(21.50m);
    }
}

public class ItemEstoqueTests
{
    [Fact]
    public void Entrada_e_perda_alteram_saldo()
    {
        var e = new ItemEstoque(Guid.NewGuid(), Guid.NewGuid(), minimo: 5);
        e.Entrada(10);
        e.RegistrarPerda(3);
        e.Quantidade.ShouldBe(7);
        e.Disponivel.ShouldBe(7);
        e.AbaixoDoMinimo.ShouldBeFalse();
        e.RegistrarPerda(2);
        e.AbaixoDoMinimo.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Entrada_deve_ser_positiva(int q) =>
        Should.Throw<DomainException>(() => new ItemEstoque(Guid.NewGuid(), Guid.NewGuid()).Entrada(q)).Codigo.ShouldBe("QUANTIDADE_INVALIDA");

    [Fact]
    public void Ajuste_nao_pode_deixar_saldo_negativo()
    {
        var e = new ItemEstoque(Guid.NewGuid(), Guid.NewGuid());
        e.Entrada(3);
        Should.Throw<DomainException>(() => e.Ajustar(-4)).Codigo.ShouldBe("AJUSTE_INVALIDO");
    }

    [Fact]
    public void Minimo_negativo_e_rejeitado() =>
        Should.Throw<DomainException>(() => new ItemEstoque(Guid.NewGuid(), Guid.NewGuid(), -1));
}

public class PagamentoTests
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.UtcNow;

    [Fact]
    public void Fluxo_aprovado_e_estorno()
    {
        var p = new Pagamento(Guid.NewGuid(), 30m, MetodoPagamento.Pix, Agora);
        p.RegistrarEnvio("tx_1", Agora);
        p.Aprovar("tx_1", Agora);
        p.Status.ShouldBe(StatusPagamento.Aprovado);
        p.Estornar(Agora);
        p.Status.ShouldBe(StatusPagamento.Estornado);
    }

    [Fact]
    public void Nao_aprova_pagamento_ja_recusado()
    {
        var p = new Pagamento(Guid.NewGuid(), 30m, MetodoPagamento.Pix, Agora);
        p.Recusar("Saldo insuficiente", Agora);
        Should.Throw<DomainException>(() => p.Aprovar("tx", Agora)).Codigo.ShouldBe("PAGAMENTO_STATUS_INVALIDO");
    }

    [Fact]
    public void Nao_estorna_pagamento_pendente() =>
        Should.Throw<DomainException>(() => new Pagamento(Guid.NewGuid(), 1m, MetodoPagamento.Pix, Agora).Estornar(Agora))
            .Codigo.ShouldBe("ESTORNO_INVALIDO");

    [Fact]
    public void Falha_no_estorno_fica_registrada()
    {
        var p = new Pagamento(Guid.NewGuid(), 30m, MetodoPagamento.CartaoCredito, Agora);
        p.Aprovar("tx", Agora);
        p.MarcarEstornoPendente("Gateway fora", Agora);
        p.Status.ShouldBe(StatusPagamento.EstornoPendente);
        p.Detalhe.ShouldBe("Gateway fora");
    }

    [Fact]
    public void Estorno_pendente_so_a_partir_de_pagamento_aprovado() =>
        Should.Throw<DomainException>(() => new Pagamento(Guid.NewGuid(), 1m, MetodoPagamento.Pix, Agora).MarcarEstornoPendente("x", Agora))
            .Codigo.ShouldBe("ESTORNO_INVALIDO");
}

public class UsuarioTests
{
    [Theory]
    [InlineData(Perfil.Gerente)]
    [InlineData(Perfil.Atendente)]
    [InlineData(Perfil.Cozinha)]
    [InlineData(Perfil.Totem)]
    public void Perfis_de_loja_exigem_unidade(Perfil perfil) =>
        Should.Throw<DomainException>(() => new Usuario("Fulano", "f@x.com", "hash", perfil, null, DateTimeOffset.UtcNow)).Codigo.ShouldBe("UNIDADE_OBRIGATORIA");

    [Fact]
    public void Cliente_nao_tem_unidade() =>
        Should.Throw<DomainException>(() => new Usuario("Fulano", "f@x.com", "hash", Perfil.Cliente, Guid.NewGuid(), DateTimeOffset.UtcNow));

    [Fact]
    public void Email_normalizado_e_anonimizacao()
    {
        var u = new Usuario("Maria", " Maria@Exemplo.COM ", "hash", Perfil.Cliente, null, DateTimeOffset.UtcNow);
        u.Email.ShouldBe("maria@exemplo.com");
        u.Anonimizar("outro-hash");
        u.Nome.ShouldBe("Cliente anonimizado");
        u.Email.ShouldNotContain("maria");
        u.Ativo.ShouldBeFalse();
    }
}
