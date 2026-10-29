using System.Text.Json;
using Ambrosio.ApiTests.Fixtures;
using Ambrosio.ApiTests.Helpers;
using FluentAssertions;

namespace Ambrosio.ApiTests.McpServer;

/// <summary>
/// T061 [US5]: o álbum de fotos pela conversa (contracts/mcp-tools.md). As ferramentas chamam o mesmo
/// <c>ICustomerPhotoService</c> da API, então cada resultado é conferido contra a resposta REST da mesma
/// vendedora — é assim que se prova que tela, rota e conversa obedecem à mesma regra (FR-023).
/// <para>
/// <b>Nada aqui gera imagem nem decodifica conteúdo</b> (research R2): os arquivos são os fixtures
/// versionados com sufixo GUID (<see cref="TestDataHelper.UniquePhotoBytes"/>), entregues ao host como
/// <c>download_url</c> https — o mesmo caminho de <see cref="ImageToolsTests"/>. A única recusa testada é
/// de <b>tipo declarado</b> fora da lista, com a mensagem literal da 004.
/// </para>
/// <para>
/// Rodam contra a stack de homologação (API + host do MCP + storage): a suíte inteira é HTTP, sem
/// container de teste.
/// </para>
/// </summary>
[Collection(ApiTestCollection.API_TESTS)]
public class CustomerPhotoToolsTests(ApiTestFixture fixture)
{
    private const string PhotoScopes = "openid email profile customers:read images:write";

    /// <summary>Frase do contrato §6, repetida da ferramenta: o enquadramento é da tela.</summary>
    private const string ProfileWithoutCropNotice =
        "A foto de perfil ficou sem recorte quadrado: para enquadrar no rosto, use a tela do Ambrosio.";

    /// <summary>Mensagem literal da 004 (<c>ImageRejectedException.TYPE_MESSAGE</c>) para tipo aceito ausente.</summary>
    private const string TypeRejectedMessage = "O conteúdo do arquivo não é uma imagem de tipo aceito.";

    // ---------------------------------------------------------------------
    // anexar_fotos_cliente (§2)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task AnexarFotos_PreviewMeasuresWithoutStoring_ConfirmStores_AndRepeatingIsJaExistia()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Anexar", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Maria Anexar");
        var fotos = new[]
        {
            new
            {
                download_url = await PublicFileUrlAsync("small.jpg"),
                mime_type = "image/jpeg",
                file_name = "IMG_0143.jpg"
            }
        };

        // Prévia: baixa para medir tipo e tamanho, não grava nada — o mesmo contrato de enviar_imagem.
        var previewCall = await seller.CallAsync("anexar_fotos_cliente", new { cliente_id = customerId, fotos });
        var preview = WriteToolsTests.Structured(previewCall);

        preview.GetProperty("gravado").GetBoolean().Should().BeFalse();
        preview.GetProperty("previa").GetProperty("cliente").GetString().Should().Be("Maria Anexar");

        var medido = preview.GetProperty("previa").GetProperty("fotos")[0];
        medido.GetProperty("nome").GetString().Should().Be("IMG_0143.jpg");
        medido.GetProperty("tipo").GetString().Should().Be("image/jpeg");
        medido.GetProperty("tamanhoBytes").GetInt64().Should().BeGreaterThan(0);
        medido.TryGetProperty("motivo", out _).Should().BeFalse("arquivo válido não chega com motivo");
        previewCall.Text.Should().Contain("Nada foi gravado");
        (await AlbumCountAsync(seller, customerId)).Should().Be(0);

        var stored = WriteToolsTests.Structured(await seller.CallAsync(
            "anexar_fotos_cliente", new { cliente_id = customerId, fotos, descricao = "provando o vestido azul", confirmar = true }));

        stored.GetProperty("gravado").GetBoolean().Should().BeTrue();
        var guardada = stored.GetProperty("registro").GetProperty("fotos")[0];
        guardada.GetProperty("resultado").GetString().Should().Be("guardada", guardada.ToString());
        guardada.GetProperty("imagemId").GetInt64().Should().BeGreaterThan(0);
        guardada.GetProperty("nome").GetString().Should().Be("IMG_0143.jpg");
        var photoId = guardada.GetProperty("fotoId").GetInt64();
        stored.GetProperty("registro").GetProperty("guardadas").GetInt32().Should().Be(1);
        (await AlbumCountAsync(seller, customerId)).Should().Be(1);

        // A mesma foto de novo na mesma cliente não abre segunda linha (FR-006): volta ja_existia, com o
        // mesmo fotoId, e a legenda do reenvio não mexe na que estava gravada.
        var repeated = WriteToolsTests.Structured(await seller.CallAsync(
            "anexar_fotos_cliente", new { cliente_id = customerId, fotos, descricao = "outra legenda", confirmar = true }));

        var repetida = repeated.GetProperty("registro").GetProperty("fotos")[0];
        repetida.GetProperty("resultado").GetString().Should().Be("ja_existia", repetida.ToString());
        repetida.GetProperty("fotoId").GetInt64().Should().Be(photoId);
        repeated.GetProperty("registro").GetProperty("repetidas").GetInt32().Should().Be(1);
        (await AlbumCountAsync(seller, customerId)).Should().Be(1);

        var page = WriteToolsTests.Structured(await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId }));
        page.GetProperty("itens")[0].GetProperty("descricao").GetString().Should().Be("provando o vestido azul",
            "a legenda é da foto, não do reenvio (CustomerPhotoService)");
    }

    [Fact]
    public async Task AnexarFotos_PreviewAlreadyShowsThe004RefusalOfADeclaredTypeOutsideTheList()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Previa Recusa", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Neusa Prévia");
        var fotos = new[]
        {
            new { download_url = await PublicFileUrlAsync("small.jpg"), mime_type = "image/gif", file_name = "foto.gif" }
        };

        var preview = WriteToolsTests.Structured(
            await seller.CallAsync("anexar_fotos_cliente", new { cliente_id = customerId, fotos }));

        preview.GetProperty("previa").GetProperty("fotos")[0].GetProperty("motivo").GetString()
            .Should().Be(TypeRejectedMessage,
                "o motivo é a mensagem literal da 004, nunca uma frase inventada na ferramenta (research R2)");
        (await AlbumCountAsync(seller, customerId)).Should().Be(0);
    }

    [Fact]
    public async Task AnexarFotos_ConfirmedRefusal_UsesThe004MessageAsMotivo_AndStoresNothing()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Recusa", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Neusa Recusa");
        var fotos = new[]
        {
            new { download_url = await PublicFileUrlAsync("small.jpg"), mime_type = "image/gif", file_name = "foto.gif" }
        };

        var result = WriteToolsTests.Structured(
            await seller.CallAsync("anexar_fotos_cliente", new { cliente_id = customerId, fotos, confirmar = true }));

        var registro = result.GetProperty("registro");
        var item = registro.GetProperty("fotos")[0];

        item.GetProperty("resultado").GetString().Should().Be("recusada", item.ToString());
        item.GetProperty("motivo").GetString().Should().Be(TypeRejectedMessage);
        item.TryGetProperty("fotoId", out _).Should().BeFalse("recusada não tem foto");
        registro.GetProperty("recusadas").GetInt32().Should().Be(1);
        registro.GetProperty("guardadas").GetInt32().Should().Be(0);
        (await AlbumCountAsync(seller, customerId)).Should().Be(0);
    }

    [Fact]
    public async Task AnexarFotos_OneBadFileInTheBatch_StoresTheRest()
    {
        // FR-021 / cenário US5-4: um arquivo recusado não interrompe os demais.
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Lote", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Lote Misto");

        var fotos = new[]
        {
            new { download_url = await PublicFileUrlAsync("small.jpg"), mime_type = "image/jpeg", file_name = "um.jpg" },
            new { download_url = await PublicFileUrlAsync("small.png"), mime_type = "image/gif", file_name = "doi.gif" },
            new { download_url = await PublicFileUrlAsync("small.webp"), mime_type = "image/webp", file_name = "tres.webp" }
        };

        var result = WriteToolsTests.Structured(await seller.CallAsync(
            "anexar_fotos_cliente", new { cliente_id = customerId, fotos, confirmar = true }));

        var registro = result.GetProperty("registro");
        var itens = registro.GetProperty("fotos").EnumerateArray().ToList();

        itens[0].GetProperty("resultado").GetString().Should().Be("guardada", itens[0].ToString());
        itens[1].GetProperty("resultado").GetString().Should().Be("recusada", itens[1].ToString());
        itens[1].GetProperty("motivo").GetString().Should().Be(TypeRejectedMessage);
        itens[2].GetProperty("resultado").GetString().Should().Be("guardada", itens[2].ToString());

        registro.GetProperty("guardadas").GetInt32().Should().Be(2);
        registro.GetProperty("recusadas").GetInt32().Should().Be(1);
        (await AlbumCountAsync(seller, customerId)).Should().Be(2);

        var guardados = itens.Where(i => i.GetProperty("resultado").GetString() == "guardada")
            .Select(i => i.GetProperty("imagemId").GetInt64())
            .ToArray();

        var page = WriteToolsTests.Structured(await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId }));
        page.GetProperty("total").GetInt32().Should().Be(2);
        page.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("imagemId").GetInt64())
            .Should().BeEquivalentTo(guardados);
    }

    [Fact]
    public async Task AnexarFotos_AboveTenFiles_IsRefusedBeforeDownloadingAnything()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Demais", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Dez Mais Uma");

        var fotos = Enumerable.Range(1, 11)
            .Select(i => new
            {
                download_url = $"https://exemplo.com/foto-{i}.jpg",
                mime_type = "image/jpeg",
                file_name = $"foto-{i}.jpg"
            })
            .ToArray();

        foreach (var confirmar in new[] { false, true })
        {
            var result = await seller.CallAsync(
                "anexar_fotos_cliente", new { cliente_id = customerId, fotos, confirmar });

            result.IsToolError.Should().BeTrue();
            result.Text.Should().Contain("no máximo 10");
        }

        (await AlbumCountAsync(seller, customerId)).Should().Be(0);
    }

    [Fact]
    public async Task AnexarFotos_OnMissingCustomer_StoresNothing_AndUsesTheApiMessage()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Sem Cliente", PhotoScopes);

        var result = await seller.CallAsync("anexar_fotos_cliente", new
        {
            cliente_id = 999_999_999,
            fotos = new[]
            {
                new { download_url = await PublicFileUrlAsync("small.jpg"), mime_type = "image/jpeg", file_name = "sozinha.jpg" }
            },
            confirmar = true
        });

        result.IsToolError.Should().BeTrue();

        // FR-028 com a frase da rota REST (CustomerPhotoService), não a do serviço de clientes.
        result.Text.Should().Be("Cliente não encontrado.");
    }

    [Fact]
    public async Task AnexarFotos_WithoutImagesWriteScope_ReturnsTokenChallenge()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Só Leitura");
        var (customerId, _) = await seller.CreateCustomerAsync("Só Leitura");
        var fotos = new[]
        {
            new { download_url = await PublicFileUrlAsync("small.jpg"), mime_type = "image/jpeg", file_name = "sem-escopo.jpg" }
        };

        var denied = await seller.CallAsync("anexar_fotos_cliente", new { cliente_id = customerId, fotos, confirmar = true });

        denied.IsToolError.Should().BeTrue();
        denied.ToolChallenge.Should().Contain("error=\"insufficient_scope\"")
            .And.Contain("error_description=")
            .And.Contain("scope=\"images:write\"");

        (await AlbumCountAsync(seller, customerId)).Should().Be(0, "o desafio devolve antes de a ferramenta rodar");
    }

    // ---------------------------------------------------------------------
    // tools/list: openai/fileParams (T057) e as anotações (§1, §8)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ToolsList_DeclaresOpenAiFileParams_OnlyForAnexarFotos()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Descritor", PhotoScopes);
        var tools = (await seller.Mcp.ListToolsAsync(seller.McpToken)).Result!.Value
            .GetProperty("tools").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => t);

        var anexar = tools["anexar_fotos_cliente"];

        anexar.GetProperty("_meta").GetProperty("openai/fileParams").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("fotos");

        // O nome declarado tem de existir no schema: sem o argumento correspondente, o modelo mandaria o
        // arquivo para um campo que a ferramenta não lê (AmbrosioFileParamsAttribute).
        anexar.GetProperty("inputSchema").GetProperty("properties").GetProperty("fotos")
            .GetProperty("type").GetString().Should().Be("array");

        anexar.GetProperty("annotations").GetProperty("openWorldHint").GetBoolean().Should().BeTrue(
            "baixar o arquivo da conversa é abrir o mundo");
        anexar.GetProperty("annotations").GetProperty("idempotentHint").GetBoolean().Should().BeTrue(
            "o mesmo lote reenviado não duplica");

        var listar = tools["listar_fotos_cliente"];
        listar.GetProperty("_meta").TryGetProperty("openai/fileParams", out _).Should().BeFalse();
        listar.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().Should().BeTrue();
        listar.GetProperty("securitySchemes")[0].GetProperty("scopes").EnumerateArray()
            .Select(s => s.GetString()).Should().Equal("customers:read");

        tools["excluir_foto_cliente"].GetProperty("annotations").GetProperty("destructiveHint")
            .GetBoolean().Should().BeTrue("apagar a foto é a única operação destrutiva do álbum (FR-027)");
        tools["definir_foto_perfil"].GetProperty("securitySchemes")[0].GetProperty("scopes")
            .EnumerateArray().Select(s => s.GetString()).Should().Equal("images:write");
    }

    // ---------------------------------------------------------------------
    // listar_fotos_cliente (§1) — sem URL de arquivo na saída
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ListarFotos_NoFileUrlInOutput_AndRespectsFavoritasAndCursor()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Grade", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Dina Grade");

        var primeira = await AnexarAsync(seller, customerId);
        var segunda = await AnexarAsync(seller, customerId, "small.png");

        var list = await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId, tamanho = 1 });
        var page = WriteToolsTests.Structured(list);

        list.RawBody.Should().NotContain("https://",
            "a conversa não recebe link de arquivo nenhum (FR-026, §1)");

        page.GetProperty("total").GetInt32().Should().Be(2, "total é o álbum inteiro, não a página");
        page.GetProperty("tem_mais").GetBoolean().Should().BeTrue();
        var cursor = page.GetProperty("proximoCursor").GetInt64();
        cursor.Should().Be(segunda.PhotoId, "a grade vai da mais recente para a mais antiga");

        var next = WriteToolsTests.Structured(await seller.CallAsync(
            "listar_fotos_cliente", new { cliente_id = customerId, tamanho = 1, cursor }));

        next.GetProperty("itens").GetArrayLength().Should().Be(1);
        next.GetProperty("itens")[0].GetProperty("fotoId").GetInt64().Should().Be(primeira.PhotoId);
        next.GetProperty("itens")[0].GetProperty("incluidaEm").GetString().Should().NotBeNullOrWhiteSpace();
        next.GetProperty("itens")[0].TryGetProperty("url", out _).Should().BeFalse();

        // O host não escreve campo nulo (JsonOptions.WhenWritingNull): no fim da lista o cursor simplesmente
        // não vem, e `tem_mais` é o sinal.
        next.TryGetProperty("proximoCursor", out _).Should().BeFalse("fim da lista");
        next.GetProperty("tem_mais").GetBoolean().Should().BeFalse();

        // Coração pela conversa, filtro pela conversa (FR-008/FR-010): ferramenta reversível, grava direto.
        var favorite = WriteToolsTests.Structured(
            await seller.CallAsync("favoritar_foto_cliente", new { cliente_id = customerId, foto_id = primeira.PhotoId }));
        favorite.GetProperty("gravado").GetBoolean().Should().BeTrue("sem prévia: é reversível (research R11)");
        favorite.GetProperty("foto").GetProperty("favorita").GetBoolean().Should().BeTrue();

        // Idempotente: favoritar de novo não é erro e não muda nada (SC-008).
        WriteToolsTests.Structured(
            await seller.CallAsync("favoritar_foto_cliente", new { cliente_id = customerId, foto_id = primeira.PhotoId }));

        var onlyFavorites = WriteToolsTests.Structured(
            await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId, favoritas = true }));
        onlyFavorites.GetProperty("itens").GetArrayLength().Should().Be(1);
        onlyFavorites.GetProperty("itens")[0].GetProperty("fotoId").GetInt64().Should().Be(primeira.PhotoId);

        var unfavorite = WriteToolsTests.Structured(
            await seller.CallAsync("desfavoritar_foto_cliente", new { cliente_id = customerId, foto_id = primeira.PhotoId }));
        unfavorite.GetProperty("foto").GetProperty("favorita").GetBoolean().Should().BeFalse();

        var noneFavorite = WriteToolsTests.Structured(
            await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId, favoritas = true }));
        noneFavorite.GetProperty("itens").GetArrayLength().Should().Be(0);
        noneFavorite.GetProperty("total").GetInt32().Should().Be(2, "total continua sendo o do álbum");
    }

    [Fact]
    public async Task ListarFotos_OnEmptyAlbum_ReturnsEmptyItemsAndNullCursor()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Vazia", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Sem Foto Nenhuma");

        var result = await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId });
        var page = WriteToolsTests.Structured(result);

        page.GetProperty("itens").GetArrayLength().Should().Be(0);
        page.GetProperty("total").GetInt32().Should().Be(0);
        page.TryGetProperty("proximoCursor", out _).Should().BeFalse("sem próxima página o campo nulo não é escrito");
        result.Text.Should().Contain("Nenhuma foto");
        result.RawBody.Should().NotContain("https://");
    }

    [Fact]
    public async Task DescreverFoto_GravaDiretoELegendaVaziaApaga()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Legenda", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Lia Legenda");
        var (photoId, _) = await AnexarAsync(seller, customerId);

        var written = WriteToolsTests.Structured(await seller.CallAsync(
            "descrever_foto_cliente", new { cliente_id = customerId, foto_id = photoId, descricao = "usou no casamento" }));

        written.GetProperty("gravado").GetBoolean().Should().BeTrue("ferramenta reversível executa sem prévia");
        written.GetProperty("foto").GetProperty("descricao").GetString().Should().Be("usou no casamento");

        var cleared = WriteToolsTests.Structured(await seller.CallAsync(
            "descrever_foto_cliente", new { cliente_id = customerId, foto_id = photoId, descricao = "  " }));

        cleared.GetProperty("foto").TryGetProperty("descricao", out _).Should().BeFalse("sem legenda não há campo");

        var page = WriteToolsTests.Structured(await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId }));
        page.GetProperty("itens")[0].TryGetProperty("descricao", out _).Should().BeFalse();

        // O limite da API vale na conversa, com a mesma mensagem (FR-007).
        var tooLong = await seller.CallAsync(
            "descrever_foto_cliente", new { cliente_id = customerId, foto_id = photoId, descricao = new string('a', 501) });

        tooLong.IsToolError.Should().BeTrue();
        tooLong.Text.Should().Be("A descrição deve ter no máximo 500 caracteres.");
    }

    [Fact]
    public async Task PhotoTools_OnAnotherSellersAlbum_UseTheSameNotFoundMessageAsTheApi()
    {
        var ana = await McpSeller.CreateAsync(fixture, "Ana Álbum", PhotoScopes);
        var bia = await McpSeller.CreateAsync(fixture, "Bia Álbum", PhotoScopes);
        var (anaCustomer, _) = await ana.CreateCustomerAsync("Cliente da Ana");
        var (photoId, _) = await AnexarAsync(ana, anaCustomer);

        foreach (var (tool, args) in new (string, object)[]
        {
            ("listar_fotos_cliente", new { cliente_id = anaCustomer }),
            ("favoritar_foto_cliente", new { cliente_id = anaCustomer, foto_id = photoId }),
            ("anexar_fotos_cliente", new { cliente_id = anaCustomer, fotos = new[] { new { download_url = "https://exemplo.com/x.jpg", mime_type = "image/jpeg", file_name = "x.jpg" } }, confirmar = true }),
            ("excluir_foto_cliente", new { cliente_id = anaCustomer, foto_id = photoId, confirmar = true })
        })
        {
            var result = await bia.CallAsync(tool, args);

            result.IsToolError.Should().BeTrue($"{tool} sobre dado de outra vendedora");
            result.Text.Should().Be("Cliente não encontrado.",
                "a conversa não revela que o recurso existe (SC-006, FR-025)");
        }

        (await AlbumCountAsync(ana, anaCustomer)).Should().Be(1, "nada mudou no álbum da Ana");
    }

    // ---------------------------------------------------------------------
    // excluir_foto_cliente (§8) — a única destrutiva
    // ---------------------------------------------------------------------

    [Fact]
    public async Task ExcluirFoto_WithoutConfirmKeepsThePhoto_ConfirmRemovesItAndTheOrphanFile()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Excluir", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Elisa Excluir");
        var (photoId, imageId) = await AnexarAsync(seller, customerId);

        var previewFoto = await seller.CallAsync("excluir_foto_cliente", new { cliente_id = customerId, foto_id = photoId });
        var preview = WriteToolsTests.Structured(previewFoto);

        preview.GetProperty("gravado").GetBoolean().Should().BeFalse("destructive exige confirmar=true (FR-027)");
        preview.GetProperty("previa").GetProperty("cliente").GetString().Should().Be("Elisa Excluir");

        var foto = preview.GetProperty("previa").GetProperty("foto");
        foto.GetProperty("fotoId").GetInt64().Should().Be(photoId);
        foto.GetProperty("imagemId").GetInt64().Should().Be(imageId);
        foto.GetProperty("favorita").GetBoolean().Should().BeFalse();
        foto.GetProperty("perfil").GetBoolean().Should().BeFalse();
        previewFoto.Text.Should().Contain("Nada foi gravado");
        (await AlbumCountAsync(seller, customerId)).Should().Be(1);

        var deleted = WriteToolsTests.Structured(
            await seller.CallAsync("excluir_foto_cliente", new { cliente_id = customerId, foto_id = photoId, confirmar = true }));

        var registro = deleted.GetProperty("registro");
        registro.GetProperty("excluida").GetBoolean().Should().BeTrue();
        registro.GetProperty("arquivo").GetString().Should().Be("removido",
            "ninguém mais usava o arquivo: a 009 o recolheu como órfão");
        registro.TryGetProperty("perfil", out _).Should().BeFalse("a foto não era o perfil");
        (await AlbumCountAsync(seller, customerId)).Should().Be(0);

        // O mesmo id agora responde a frase da API para foto ausente, na prévia e no confirmado.
        foreach (var confirmar in new[] { false, true })
        {
            var gone = await seller.CallAsync(
                "excluir_foto_cliente", new { cliente_id = customerId, foto_id = photoId, confirmar });

            gone.IsToolError.Should().BeTrue();
            gone.Text.Should().Be("Foto não encontrada.");
        }

        // E o arquivo saiu mesmo do storage: a rota da 004 não o encontra mais.
        (await fixture.GetAsync($"/images/{imageId}", seller.User.ApiToken)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task ExcluirFoto_KeepsTheFileWhenAnotherCustomerUsesIt()
    {
        // Edge case "mesma foto em duas clientes": o arquivo continua porque a outra linha o usa (R6).
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Duas Clientes", PhotoScopes);
        var (ana, _) = await seller.CreateCustomerAsync("Ana Compartilha");
        var (bruna, _) = await seller.CreateCustomerAsync("Bruna Compartilha");

        var fotos = new[]
        {
            new { download_url = await PublicFileUrlAsync("other.jpg"), mime_type = "image/jpeg", file_name = "compartilhada.jpg" }
        };

        var paraAna = WriteToolsTests.Structured(await seller.CallAsync(
            "anexar_fotos_cliente", new { cliente_id = ana, fotos, confirmar = true }));
        paraAna.GetProperty("registro").GetProperty("fotos")[0].GetProperty("resultado").GetString().Should().Be("guardada");

        var paraBruna = WriteToolsTests.Structured(await seller.CallAsync(
            "anexar_fotos_cliente", new { cliente_id = bruna, fotos, confirmar = true }));

        var segunda = paraBruna.GetProperty("registro").GetProperty("fotos")[0];
        segunda.GetProperty("resultado").GetString().Should().Be("guardada_tambem_em_outra_cliente",
            "a imagem já estava no álbum de outra cliente da mesma vendedora");
        var photoIdDeBruna = segunda.GetProperty("fotoId").GetInt64();
        var imageId = segunda.GetProperty("imagemId").GetInt64();

        var deleted = WriteToolsTests.Structured(await seller.CallAsync(
            "excluir_foto_cliente", new { cliente_id = bruna, foto_id = photoIdDeBruna, confirmar = true }));

        deleted.GetProperty("registro").GetProperty("arquivo").GetString().Should().Be("mantido (em uso)");
        (await AlbumCountAsync(seller, ana)).Should().Be(1, "a foto da Ana continua no álbum dela");
        (await fixture.GetAsync($"/images/{imageId}", seller.User.ApiToken)).StatusCode.Should().Be(200,
            "o arquivo ainda serve a cliente Ana");
    }

    // ---------------------------------------------------------------------
    // definir/remover foto de perfil (§6, §7) e a saída de obter_cliente (T059)
    // ---------------------------------------------------------------------

    [Fact]
    public async Task DefinirFotoPerfil_SemRecorteAvisa_EObterClienteInformaTemFotoPerfilEtotalFotos()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Perfil", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Rita Perfil");
        var (photoId, imageId) = await AnexarAsync(seller, customerId);

        var antes = WriteToolsTests.Structured(await seller.CallAsync("obter_cliente", new { cliente_id = customerId }));
        antes.GetProperty("temFotoPerfil").GetBoolean().Should().BeFalse();
        antes.GetProperty("totalFotos").GetInt32().Should().Be(1);

        var set = await seller.CallAsync("definir_foto_perfil", new { cliente_id = customerId, foto_id = photoId });
        var structured = WriteToolsTests.Structured(set);

        structured.GetProperty("aviso").GetString().Should().Be(ProfileWithoutCropNotice);
        structured.GetProperty("fotoPerfil").GetProperty("fotoId").GetInt64().Should().Be(photoId);
        structured.GetProperty("fotoPerfil").GetProperty("imagemId").GetInt64().Should().Be(imageId,
            "sem recorte o círculo usa o arquivo da própria foto (FR-013)");
        set.RawBody.Should().NotContain("https://", "o perfil da conversa também não devolve link assinado");

        // Idempotente: apontar a mesma foto de novo não cria nada nem muda os ids (§6).
        var again = WriteToolsTests.Structured(
            await seller.CallAsync("definir_foto_perfil", new { cliente_id = customerId, foto_id = photoId }));
        again.GetProperty("fotoPerfil").GetProperty("imagemId").GetInt64().Should().Be(imageId);
        (await AlbumCountAsync(seller, customerId)).Should().Be(1);

        var detail = WriteToolsTests.Structured(await seller.CallAsync("obter_cliente", new { cliente_id = customerId }));
        detail.GetProperty("temFotoPerfil").GetBoolean().Should().BeTrue();
        detail.GetProperty("totalFotos").GetInt32().Should().Be(1);

        var listedCall = await seller.CallAsync("listar_clientes", new { busca = "Rita Perfil", tamanho = 20 });
        var listed = WriteToolsTests.Structured(listedCall);
        listed.GetProperty("itens")[0].GetProperty("temFotoPerfil").GetBoolean().Should().BeTrue();
        listedCall.RawBody.Should().NotContain("https://", "o booleano chega sem nenhuma URL (T059)");

        var grade = WriteToolsTests.Structured(await seller.CallAsync("listar_fotos_cliente", new { cliente_id = customerId }));
        grade.GetProperty("itens")[0].GetProperty("perfil").GetBoolean().Should().BeTrue();

        var removed = WriteToolsTests.Structured(await seller.CallAsync("remover_foto_perfil", new { cliente_id = customerId }));
        removed.GetProperty("perfilRemovido").GetBoolean().Should().BeTrue();

        var semPerfil = WriteToolsTests.Structured(await seller.CallAsync("obter_cliente", new { cliente_id = customerId }));
        semPerfil.GetProperty("temFotoPerfil").GetBoolean().Should().BeFalse("voltou às iniciais");
        semPerfil.GetProperty("totalFotos").GetInt32().Should().Be(1, "remover o perfil não mexe no álbum (FR-015)");

        // Repetir sem perfil não é erro (§7).
        WriteToolsTests.Structured(await seller.CallAsync("remover_foto_perfil", new { cliente_id = customerId }));
    }

    [Fact]
    public async Task ExcluirFotoQueEoPerfil_LimpaOPerfilEAvisaNaResposta()
    {
        var seller = await McpSeller.CreateAsync(fixture, "Vendedora Perfil Órfão", PhotoScopes);
        var (customerId, _) = await seller.CreateCustomerAsync("Tereza Perfil");
        var (photoId, _) = await AnexarAsync(seller, customerId);

        WriteToolsTests.Structured(
            await seller.CallAsync("definir_foto_perfil", new { cliente_id = customerId, foto_id = photoId }));

        var deleted = WriteToolsTests.Structured(await seller.CallAsync(
            "excluir_foto_cliente", new { cliente_id = customerId, foto_id = photoId, confirmar = true }));

        deleted.GetProperty("registro").GetProperty("perfil").GetString()
            .Should().Be("a cliente voltou a mostrar as iniciais", "a foto excluída era a origem do perfil (FR-015)");

        var detail = WriteToolsTests.Structured(await seller.CallAsync("obter_cliente", new { cliente_id = customerId }));
        detail.GetProperty("temFotoPerfil").GetBoolean().Should().BeFalse();
        detail.GetProperty("totalFotos").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task DefinirFotoPerfil_FotoDeOutraCliente_NaoGravaNada()
    {
        var ana = await McpSeller.CreateAsync(fixture, "Ana Dono do Perfil", PhotoScopes);
        var bia = await McpSeller.CreateAsync(fixture, "Bia Invasora", PhotoScopes);
        var (anaCustomer, _) = await ana.CreateCustomerAsync("Cliente da Ana Perfil");
        var (photoId, _) = await AnexarAsync(ana, anaCustomer);
        var (biaCustomer, _) = await bia.CreateCustomerAsync("Cliente da Bia");

        var result = await bia.CallAsync("definir_foto_perfil", new { cliente_id = biaCustomer, foto_id = photoId });

        result.IsToolError.Should().BeTrue();
        result.Text.Should().Be("Foto não encontrada.", "a foto é do álbum de outra cliente (FR-025)");

        var detail = WriteToolsTests.Structured(await bia.CallAsync("obter_cliente", new { cliente_id = biaCustomer }));
        detail.GetProperty("temFotoPerfil").GetBoolean().Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Apoio
    // ---------------------------------------------------------------------

    private sealed record Attached(long PhotoId, long ImageId);

    /// <summary>
    /// Anexa um arquivo da conversa pelo próprio <c>anexar_fotos_cliente</c> (confirmado) e devolve os
    /// ids — o setup de quem testa favorito, perfil e exclusão, sem repetir a mecânica de gravação.
    /// </summary>
    private async Task<Attached> AnexarAsync(McpSeller seller, long customerId, string fileName = "small.jpg")
    {
        var fotos = new[]
        {
            new
            {
                download_url = await PublicFileUrlAsync(fileName),
                mime_type = MimeTypeOf(fileName),
                file_name = fileName
            }
        };

        var result = WriteToolsTests.Structured(await seller.CallAsync(
            "anexar_fotos_cliente", new { cliente_id = customerId, fotos, confirmar = true }));

        var item = result.GetProperty("registro").GetProperty("fotos")[0];

        item.GetProperty("resultado").GetString().Should().Be("guardada", item.ToString());

        return new Attached(item.GetProperty("fotoId").GetInt64(), item.GetProperty("imagemId").GetInt64());
    }

    /// <summary>
    /// Guarda o fixture em uma conta <b>separada</b> pela rota da 004 e devolve a URL pré-assinada de
    /// leitura: um arquivo https público com bytes inéditos, como o <c>download_url</c> que o ChatGPT
    /// entrega. Separada de propósito — a deduplicação da 004 é por (usuário, sha256), então a vendedora
    /// do teste precisa chegar com conteúdo novo para ela (<see cref="ImageToolsTests"/>).
    /// </summary>
    private async Task<string> PublicFileUrlAsync(string fileName = "small.jpg")
    {
        var owner = await McpTestUser.CreateAsync(fixture, "Dona do Arquivo");
        var bytes = TestDataHelper.UniquePhotoBytes(fileName);

        var upload = await fixture.PostFileAsync("/images", owner.ApiToken, bytes, MimeTypeOf(fileName), fileName);
        upload.IsSuccess.Should().BeTrue(upload.Body);

        using var created = JsonDocument.Parse(upload.Body);
        var imageId = created.RootElement.GetProperty("imageId").GetInt64();

        var read = await fixture.GetAsync($"/images/{imageId}", owner.ApiToken);
        using var image = JsonDocument.Parse(read.Body);

        return image.RootElement.GetProperty("url").GetString()!;
    }

    /// <summary>Quantas fotos o álbum tem, pela API REST — a mesma fonte da grade da tela.</summary>
    private async Task<int> AlbumCountAsync(McpSeller seller, long customerId)
    {
        using var page = await seller.RestGetAsync(TestDataHelper.PhotosPath(customerId));

        return page.RootElement.GetProperty("items").GetArrayLength();
    }

    private static string MimeTypeOf(string fileName) => fileName switch
    {
        "small.png" => "image/png",
        "small.webp" => "image/webp",
        _ => "image/jpeg"
    };
}
