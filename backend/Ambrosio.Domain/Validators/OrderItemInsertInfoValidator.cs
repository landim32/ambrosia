using Ambrosio.DTO.Orders;
using FluentValidation;

namespace Ambrosio.Domain.Validators;

/// <summary>
/// Validação de uma linha de pedido (FR-015). Os limites são as colunas de
/// <c>order_items</c> (data-model): product_name varchar(200), sku_ref varchar(60),
/// size varchar(20), image_url varchar(500).
/// </summary>
public class OrderItemInsertInfoValidator : AbstractValidator<OrderItemInsertInfo>
{
    public OrderItemInsertInfoValidator()
    {
        // Ao menos UM dos identificadores do produto: o +Vendas nem sempre traz nome e SKU juntos,
        // e uma linha sem nenhum dos dois não descreve nada (FR-015).
        RuleFor(x => x.ProductName)
            .Must((item, productName) => !string.IsNullOrWhiteSpace(productName)
                                        || !string.IsNullOrWhiteSpace(item.SkuRef))
                .WithMessage("Informe o nome do produto ou a referência (SKU).")
            .MaximumLength(200)
                .WithMessage("O nome do produto deve ter no máximo 200 caracteres.");

        RuleFor(x => x.SkuRef)
            .MaximumLength(60)
                .WithMessage("A referência (SKU) deve ter no máximo 60 caracteres.");

        RuleFor(x => x.Size)
            .MaximumLength(20)
                .WithMessage("O tamanho deve ter no máximo 20 caracteres.");

        // price não tem regra de sinal: é negativo em devolução/cancelamento, espelhando a
        // origem (data-model).

        // Cascade Stop: uma URL fora de tamanho não deve acumular dois erros no mesmo campo.
        RuleFor(x => x.ImageUrl)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(500)
                .WithMessage("A URL da imagem deve ter no máximo 500 caracteres.")
            .Must(IsAbsoluteHttpUrl)
                .WithMessage("A URL da imagem deve ser absoluta (http ou https).")
                .When(x => !string.IsNullOrWhiteSpace(x.ImageUrl));
    }

    /// <summary>
    /// Aceita apenas URL absoluta http/https com host. Protocol-relative
    /// (<c>//cdn.exemplo.com/...</c>), hostname solto (<c>cdn.exemplo.com/a.jpg</c>) e esquemas
    /// perigosos (<c>javascript:</c>, <c>file:</c>) são recusados: o campo existe para ser usado
    /// como &lt;img src&gt; pelo front-end futuro, e uma URL relativa poderia apontar para dentro
    /// da própria API.
    /// </summary>
    private static bool IsAbsoluteHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var parsed)
               && parsed is not null
               && !string.IsNullOrEmpty(parsed.Host)
               && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }
}
