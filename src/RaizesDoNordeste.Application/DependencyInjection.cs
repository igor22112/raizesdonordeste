using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using RaizesDoNordeste.Application.Campanhas;
using RaizesDoNordeste.Application.Catalogo;
using RaizesDoNordeste.Application.Comum;
using RaizesDoNordeste.Application.Estoques;
using RaizesDoNordeste.Application.Fidelidade;
using RaizesDoNordeste.Application.Identidade;
using RaizesDoNordeste.Application.Pagamentos;
using RaizesDoNordeste.Application.Pedidos;
using RaizesDoNordeste.Application.Privacidade;
using RaizesDoNordeste.Application.Relatorios;
using RaizesDoNordeste.Application.Unidades;

namespace RaizesDoNordeste.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddSingleton(TimeProvider.System);

        services.AddScoped<IAuditoria, ServicoAuditoria>();
        services.AddScoped<ConsultasCliente>();
        services.AddScoped<EstoqueOperacoes>();
        services.AddScoped<FidelidadeOperacoes>();
        services.AddScoped<EstornoPagamentos>();

        services.AddScoped<AutenticacaoService>();
        services.AddScoped<UnidadeService>();
        services.AddScoped<ProdutoService>();
        services.AddScoped<CardapioService>();
        services.AddScoped<EstoqueService>();
        services.AddScoped<PedidoService>();
        services.AddScoped<ExpiracaoPedidosService>();
        services.AddScoped<PagamentoService>();
        services.AddScoped<FidelidadeService>();
        services.AddScoped<CampanhaService>();
        services.AddScoped<PrivacidadeService>();
        services.AddScoped<RelatorioService>();

        return services;
    }
}
