# ECU Monitor Focus

Monitor local de diagnóstico para a **ECU Visteon 9M55AD**, usada em uma aplicação do **Ford Focus Duratec 2.0**. Versão **1.3.1**.

A proposta é acompanhar o funcionamento do motor e salvar os dados em um único CSV para analisar depois: rotação, avanço, pedal, borboleta, correções de combustível e leituras específicas do cálculo de injeção.

**É uma ferramenta de leitura. Não faz remap, não grava na ECU e não atualiza o ELM327.** O perfil contém endereços específicos: não presuma compatibilidade com outras ECUs, mesmo que sejam de um Focus ou tenham nome parecido.

## O que você precisa

- Notebook com Windows 10 ou 11, Windows PowerShell 5.1 e .NET Framework 4.x.
- Navegador moderno.
- Adaptador ELM327 USB compatível, com o driver serial já funcionando.
- Conexão à tomada OBD do carro. Em adaptadores com chave HS/MS, use **HS** para este perfil.

Não precisa instalar Python, Node.js ou bibliotecas. O funcionamento é local, sem internet. Adaptadores genéricos podem ter limitações; a identificação “v1.5” ou a etiqueta “25K80 FTDI” não garante compatibilidade.

## Como baixar e abrir

1. Clique em **Code → Download ZIP** neste repositório.
2. Extraia a pasta inteira. Não execute de dentro do ZIP.
3. Dê dois cliques em **Iniciar.cmd**.
4. O navegador abrirá o painel. Mantenha a janela do lançador aberta enquanto usa o monitor.

Para conhecer a tela sem carro ou adaptador, execute **Demonstracao.cmd** ou escolha a demonstração no painel. Os dados simulados ficam identificados como **DEMO** na tela e nos arquivos.

## Como conectar no carro

1. Com o veículo estacionado, conecte o ELM à USB e à OBD.
2. Feche FORScan, KESS e qualquer outro programa que esteja usando a mesma interface.
3. Ligue a ignição. Para a primeira conferência, pode manter o motor desligado.
4. No painel, selecione a porta **COM** do adaptador e a velocidade serial **Automática**. A COM pode mudar de um computador para outro; confira no Gerenciador de Dispositivos, em **Portas (COM e LPT)**.
5. Confirme que a ECU corresponde ao perfil analisado e clique em **Iniciar captura**.
6. Aguarde a identificação do ELM e a confirmação da PCM. Com motor desligado, uma leitura válida de rotação deve ser zero.
7. Depois de validar a conexão, dê partida em local ventilado e confira as leituras parado.

O monitor tenta CAN de 11 bits a 500 e 250 kbps, confirma respostas da PCM e o protocolo. Neste perfil, quando a consulta física de rotação é recusada com NRC 11/12, tenta OBD funcional. Não aceita uma resposta `OK` do adaptador como prova de comunicação com o motor.

## Como iniciar e parar os arquivos

- **Iniciar captura** conecta e cria automaticamente um **CSV** e um **log bruto JSONL** na pasta `logs`.
- As amostras do CSV só começam após a validação da PCM. Uma tentativa malsucedida pode deixar um CSV apenas com o cabeçalho e um log bruto com o erro.
- **Parar captura** encerra os arquivos e libera o adaptador. Os botões de download continuam disponíveis.
- Uma nova captura cria outro par de arquivos.
- Abrir a página não inicia uma captura. **Fechar somente a aba do navegador não para o serviço local.** Use **Parar captura** antes de fechar o lançador ou desconectar o ELM.

Para analisar um problema, preserve **os dois arquivos da mesma sessão**. O CSV contém os valores; o JSONL ajuda a entender as respostas e as falhas de comunicação. Revise os arquivos antes de compartilhá-los.

## O que aparece no painel

| Leitura | Como interpretar |
| --- | --- |
| Rotação | RPM informada pela PCM. |
| Avanço | Avanço informado pelo diagnóstico; não é leitura de detonação. |
| Pedal APP | Percentual interno da estratégia. Não é normalizado artificialmente para 100%. |
| Borboleta TP | Percentual absoluto do PID OBD; não representa diretamente o ângulo em graus. |
| Borboleta medida e desejada | Escala binária conhecida; a unidade física em graus ainda é uma inferência. |
| STFT e LTFT | Correções curta e longa de combustível. Interprete junto ao estado de malha aberta/fechada. |
| Correção composta | Combina STFT/LTFT apenas quando ambos foram lidos no ciclo e o sistema está em malha fechada. |
| Pulso base e offset | Valores comandados/calculados pela ECU, não uma medição elétrica com osciloscópio. O pulso base não inclui o offset separado. |
| Pressão de combustível estimada | Variável calculada pela ECU. **Não é pressão física medida comprovada por sensor.** |
| ECT, IAT, MAP e carga | Contexto de temperatura, pressão do coletor e carga calculada. |

O switch **PSI/BAR**, dentro do card de pressão, muda a apresentação. O CSV mantém **ambas as unidades**. A unidade PSI de origem é inferida; converter para bar não transforma a estimativa em uma medição. Essa leitura não serve para verificar a bomba ou a estanqueidade dos bicos: isso exige medição física adequada.

## Velocidade e falhas de comunicação

O intervalo escolhido é um mínimo desejado, não uma promessa de frequência. O ELM e a ECU determinam o tempo real. Veja `cycle_ms`, `cycle_hz` e o horário individual de cada sinal no CSV. Os sinais são consultados em sequência; a linha não é uma fotografia simultânea de todas as variáveis.

As consultas são agrupadas por endereço para reduzir comandos desnecessários. O contexto opcional alterna um sinal por ciclo. Deixe a opção de quatro canais dos bicos desativada quando não precisar dela.

Um `NO DATA` isolado durante a captura deixa o sinal vazio, marcado como `no_data`, e ele será tentado novamente. O programa não preenche essa lacuna com o valor anterior. **Três ciclos consecutivos sem resposta de rotação encerram a sessão.** Após desligar e religar a ignição, pode ser necessário iniciar uma nova captura.

Timeout sem conclusão, resposta malformada e outras falhas não são ignorados. Certas recusas de leitura RAM desativam apenas esse grupo e preservam os PIDs disponíveis; não há tentativa de desbloqueio.

## Limites do monitor

- Não mede potência, torque, lambda de wideband ou detonação.
- Não comprova segurança de uma calibração modificada.
- Valores específicos dependem da estratégia da ECU e do suporte diagnóstico do veículo.
- Testes simulados comprovam comportamento do software, não compatibilidade universal nem a precisão física de cada variável.
- Durante rodagem, deixe um passageiro operar o notebook. Faça a validação inicial parado.

## Privacidade e funcionamento local

O repositório contém código, testes simulados e documentação. **Não inclui BIN da ECU, VIN/chassi, placa, capturas do veículo, hash da leitura original, credenciais ou caminhos pessoais do ambiente de desenvolvimento.**

O servidor escuta apenas em `127.0.0.1`, com uma porta local temporária. Não há envio automático para a internet. O token de proteção é criado a cada execução e não é uma credencial publicada. Os logs permanecem no computador em que a captura foi feita.

Veja [PRIVACIDADE.md](PRIVACIDADE.md) antes de compartilhar logs ou contribuir.

## Estrutura e testes

- `Iniciar.cmd` / `Iniciar.ps1`: inicialização e servidor local.
- `Demonstracao.cmd`: modo simulado, sem abrir COM.
- `src/MonitorCore.cs`: comunicação, interpretação e CSV.
- `web/index.html`: painel.
- `tests/`: testes com canais simulados, sem acessar o veículo.

Para executar os testes, abra o Windows PowerShell na pasta do projeto:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Testar.ps1
```

A suíte incluída tem **225 verificações**. Os resultados são gerados localmente e não devem ser versionados. A interface PSI/BAR também foi conferida em navegador, incluindo teclado e layout móvel. A taxa real de captura depende do conjunto notebook/adaptador/ECU.
