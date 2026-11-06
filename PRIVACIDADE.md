# Privacidade

A publicação usa uma cópia selecionada do programa. Foram excluídos logs reais e de demonstração, BINs, projetos de calibração, capturas de tela, configurações temporárias do servidor e relatórios históricos com caminhos locais ou hashes da leitura original.

Não foram encontrados VIN/chassi, placa, credenciais ou dados pessoais do proprietário nos arquivos selecionados. Endereços de RAM e DIDs são constantes técnicas do perfil da ECU, não identificadores pessoais. O nome de usuário do proprietário do repositório é público no próprio GitHub.

Os testes usam canais simulados. Podem criar arquivos temporários e resultados no computador de quem os executa. O programa cria logs locais quando o usuário inicia uma sessão; esses arquivos podem revelar horários, funcionamento e padrões de uso do veículo. A proteção de `.gitignore` evita inclusão acidental comum, mas não substitui revisão antes de compartilhar arquivos ou usar `git add -f`.

O programa não consulta VIN nem envia logs para servidores externos. Seu servidor usa apenas loopback. O token de proteção é gerado em execução e não é persistido no código.

Antes de contribuir, exclua logs, BINs, credenciais, caminhos pessoais e identificadores de veículos. Não publique seu arquivo de ECU para reportar um problema de conexão. Prefira uma descrição e trechos estritamente necessários, revisados e anonimizados.
